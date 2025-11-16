using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.IO;
using System.Text;
using TenantDbService.Api.Auth;
using TenantDbService.Api.Catalog;
using TenantDbService.Api.Common;
using TenantDbService.Api.Data.Mongo;
using TenantDbService.Api.Data.Sql;
using TenantDbService.Api.Features.Events;
using TenantDbService.Api.Features.Orders;
using TenantDbService.Api.Middleware;
using TenantDbService.Api.Provisioning;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();

// Add HttpContextAccessor for tenant resolution
builder.Services.AddHttpContextAccessor();

// Configuration
builder.Services.Configure<JwtSettings>(builder.Configuration.GetSection("Jwt"));
builder.Services.Configure<ConnectionSettings>(builder.Configuration.GetSection("ConnectionStrings"));
builder.Services.Configure<SqlServerSettings>(builder.Configuration.GetSection("SqlServer"));
builder.Services.Configure<MongoSettings>(builder.Configuration.GetSection("Mongo"));

// Database contexts
builder.Services.AddDbContext<CatalogDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("Catalog"), 
        sqlServerOptionsAction: sqlOptions =>
        {
            sqlOptions.EnableRetryOnFailure(
                maxRetryCount: 5,
                maxRetryDelay: TimeSpan.FromSeconds(30),
                errorNumbersToAdd: null);
        }));

// Repositories and services
builder.Services.AddScoped<ICatalogRepository, CatalogRepository>();
builder.Services.AddScoped<ProvisioningService>();
builder.Services.AddScoped<OrdersRepository>();
builder.Services.AddScoped<EventsRepository>();
builder.Services.AddScoped<DynamicSchemaService>();
builder.Services.AddScoped<DynamicDataService>();

// Connection factories
builder.Services.AddScoped<SqlConnectionFactory>();
builder.Services.AddScoped<IMongoDbFactory, MongoDbFactory>();

// Auth services
builder.Services.AddScoped<JwtExtensions>();

// Caching
builder.Services.AddMemoryCache();

// JWT Authentication
var jwtSettings = builder.Configuration.GetSection("Jwt").Get<JwtSettings>();
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwtSettings?.Issuer,
            ValidAudience = jwtSettings?.Audience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSettings?.Key ?? "default-key"))
        };
    });

builder.Services.AddAuthorization();

// Swagger
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo { Title = "TenantDbService API", Version = "v1" });
    
    // Define the Bearer token security scheme
    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Description = "JWT Authorization header using the Bearer scheme. Enter 'Bearer' [space] and then your token in the text input below.\n\nExample: \"Bearer eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9...\"",
        Name = "Authorization",
        In = ParameterLocation.Header,
        Type = SecuritySchemeType.Http,
        Scheme = "Bearer",
        BearerFormat = "JWT"
    });
    
    // Apply security requirement globally to all endpoints
    c.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference
                {
                    Type = ReferenceType.SecurityScheme,
                    Id = "Bearer"
                }
            },
            Array.Empty<string>()
        }
    });
    
    // Enable XML comments if available
    var xmlFile = $"{System.Reflection.Assembly.GetExecutingAssembly().GetName().Name}.xml";
    var xmlPath = Path.Combine(AppContext.BaseDirectory, xmlFile);
    if (File.Exists(xmlPath))
    {
        c.IncludeXmlComments(xmlPath);
    }
});

// Observability
builder.Services.AddLogging();
var meter = new Meter("TenantDbService");
var counter = meter.CreateCounter<long>("requests_total");
var histogram = meter.CreateHistogram<double>("request_duration_ms");

builder.Services.AddSingleton(meter);
builder.Services.AddSingleton(counter);
builder.Services.AddSingleton(histogram);

var app = builder.Build();

// Configure the HTTP request pipeline
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "TenantDbService API v1");
        c.RoutePrefix = "swagger";
        c.DisplayRequestDuration();
        c.EnablePersistAuthorization(); // Persist authorization across page refreshes
        c.EnableDeepLinking();
        c.EnableFilter();
        c.ShowExtensions();
        c.EnableValidator();
    });
}

app.UseHttpsRedirection();

// Global exception handler
app.UseExceptionHandler("/error");

// Tenant resolution middleware (before authentication to extract tenantId from JWT)
// This allows tenant resolution even if JWT validation fails
app.UseMiddleware<TenantResolutionMiddleware>();

app.UseAuthentication();
app.UseAuthorization();

// Health endpoints
app.MapGet("/health/live", () => Results.Ok(new { status = "healthy" }));

app.MapGet("/health/ready", async (CatalogDbContext catalogDb, HttpContext context) =>
{
    try
    {
        // Check catalog database
        await catalogDb.Database.CanConnectAsync();
        
        // If tenant context exists, check tenant databases
        if (context.Items.TryGetValue("tenant.ctx", out var tenantCtxObj) && 
            tenantCtxObj is TenantContext tenantCtx)
        {
            // Check SQL Server connection
            var sqlFactory = context.RequestServices.GetRequiredService<SqlConnectionFactory>();
            using var sqlConnection = await sqlFactory.CreateConnectionAsync();
            await sqlConnection.OpenAsync();
            
            // Check MongoDB connection
            var mongoFactory = context.RequestServices.GetRequiredService<IMongoDbFactory>();
            var mongoDb = await mongoFactory.GetDatabaseAsync();
            await mongoDb.RunCommandAsync<MongoDB.Bson.BsonDocument>(new MongoDB.Bson.BsonDocument("ping", 1));
        }
        
        return Results.Ok(new { status = "ready" });
    }
    catch (Exception ex)
    {
        return Results.StatusCode(503);
    }
});

// Auth endpoints
app.MapPost("/auth/dev-token", (JwtExtensions jwtExtensions, [FromBody] DevTokenRequest request) =>
{
    if (string.IsNullOrEmpty(request.TenantId))
        return Results.BadRequest(new { error = "TenantId is required" });
    
    var token = jwtExtensions.GenerateDevToken(request.TenantId);
    return Results.Ok(new { token });
})
.WithName("GenerateDevToken");

// Tenant management endpoints
app.MapPost("/tenants", async (ProvisioningService provisioningService, [FromBody] CreateTenantRequest request) =>
{
    if (string.IsNullOrEmpty(request.Name))
        return Results.BadRequest(new { error = "Name is required" });
    
    var tenantId = await provisioningService.CreateTenantAsync(request.Name, request.SchemaDefinition);
    return Results.Ok(new { tenantId });
})
.WithName("CreateTenant");

app.MapGet("/tenants", async (ICatalogRepository catalogRepository) =>
{
    var tenants = await catalogRepository.GetAllTenantsAsync();
    return Results.Ok(tenants);
})
.WithName("ListTenants");

// Schema management endpoints
app.MapPost("/tenants/{tenantId}/schema", async (ProvisioningService provisioningService, string tenantId, [FromBody] UpdateSchemaRequest request) =>
{
    try
    {
        var schemaDefinition = System.Text.Json.JsonSerializer.Deserialize<SchemaDefinition>(request.SchemaDefinition);
        if (schemaDefinition == null)
            return Results.BadRequest(new { error = "Invalid schema definition" });
        
        await provisioningService.UpdateSchemaAsync(tenantId, schemaDefinition);
        return Results.Ok(new { message = "Schema updated successfully" });
    }
    catch (ArgumentException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
})
.RequireAuthorization()
.WithName("UpdateTenantSchema");

app.MapGet("/tenants/{tenantId}/schema", async (ICatalogRepository catalogRepository, string tenantId) =>
{
    var schema = await catalogRepository.GetSchemaAsync(tenantId);
    if (schema == null)
        return Results.NotFound(new { error = "Schema not found" });
    
    return Results.Ok(schema);
})
.RequireAuthorization()
.WithName("GetTenantSchema");

app.MapPost("/schema/validate", (DynamicSchemaService schemaService, [FromBody] SchemaValidationRequest request) =>
{
    var validation = schemaService.ValidateSchema(request.SchemaDefinition);
    return Results.Ok(new SchemaValidationResponse(validation.IsValid, validation.Errors));
})
.WithName("ValidateSchema");

// Create a single table endpoint
app.MapPost("/api/data/tables/create", async (DynamicSchemaService schemaService, SqlConnectionFactory sqlFactory, [FromBody] TableDefinition tableDefinition) =>
{
    try
    {
        // Validate table definition
        if (string.IsNullOrWhiteSpace(tableDefinition.Name))
        {
            return Results.BadRequest(new { error = "Table name is required" });
        }

        if (tableDefinition.Columns == null || !tableDefinition.Columns.Any())
        {
            return Results.BadRequest(new { error = "Table must have at least one column" });
        }

        // Check if at least one column is a primary key
        if (!tableDefinition.Columns.Any(c => c.IsPrimaryKey))
        {
            return Results.BadRequest(new { error = "Table must have at least one primary key column" });
        }

        // Validate the table definition
        var tempSchema = new SchemaDefinition
        {
            Version = "1.0",
            Name = "Temp Schema",
            Tables = new List<TableDefinition> { tableDefinition }
        };
        
        var validation = schemaService.ValidateSchema(tempSchema);
        if (!validation.IsValid)
        {
            return Results.BadRequest(new { error = "Invalid table definition", errors = validation.Errors });
        }

        // Create the table
        using var connection = await sqlFactory.CreateConnectionAsync();
        await connection.OpenAsync();
        
        await schemaService.CreateTableAsync(connection, tableDefinition);
        
        return Results.Ok(new { message = $"Table '{tableDefinition.Name}' created successfully" });
    }
    catch (Exception ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
})
.RequireAuthorization()
.WithTags("SQL Server - Dynamic Tables")
.WithName("CreateTable");

// Dynamic data access endpoints - SQL Server
app.MapGet("/api/data/tables", async (DynamicDataService dataService) =>
{
    var tables = await dataService.GetTableNamesAsync();
    return Results.Ok(tables);
})
.RequireAuthorization()
.WithTags("SQL Server - Dynamic Tables")
.WithName("GetTableNames");

app.MapGet("/api/data/tables/{tableName}/schema", async (DynamicDataService dataService, string tableName) =>
{
    var schema = await dataService.GetTableSchemaAsync(tableName);
    return Results.Ok(schema);
})
.RequireAuthorization()
.WithTags("SQL Server - Dynamic Tables")
.WithName("GetTableSchema");

app.MapGet("/api/data/tables/{tableName}", async (DynamicDataService dataService, string tableName, string? where, string? orderBy, int? limit) =>
{
    var data = await dataService.QueryAsync(tableName, where, orderBy, limit);
    return Results.Ok(data);
})
.RequireAuthorization()
.WithTags("SQL Server - Dynamic Tables")
.WithName("QueryTable");

app.MapGet("/api/data/tables/{tableName}/{id}", async (DynamicDataService dataService, string tableName, string id) =>
{
    var data = await dataService.GetByIdAsync(tableName, id);
    if (data == null)
        return Results.NotFound();
    
    return Results.Ok(data);
})
.RequireAuthorization()
.WithTags("SQL Server - Dynamic Tables")
.WithName("GetTableRecord");

app.MapPost("/api/data/tables/{tableName}", async (DynamicDataService dataService, string tableName, [FromBody] Dictionary<string, object> data, ILogger<Program> logger) =>
{
    logger.LogInformation("POST /api/data/tables/{TableName} - Received tableName: '{TableName}'", tableName, tableName);
    
    var id = await dataService.InsertAsync(tableName, data);
    return Results.Created($"/api/data/tables/{tableName}/{id}", new { id });
})
.RequireAuthorization()
.WithTags("SQL Server - Dynamic Tables")
.WithName("InsertTableRecord");

app.MapPut("/api/data/tables/{tableName}/{id}", async (DynamicDataService dataService, string tableName, string id, [FromBody] Dictionary<string, object> data) =>
{
    var success = await dataService.UpdateAsync(tableName, id, data);
    if (!success)
        return Results.NotFound();
    
    return Results.Ok(new { message = "Updated successfully" });
})
.RequireAuthorization()
.WithTags("SQL Server - Dynamic Tables")
.WithName("UpdateTableRecord");

app.MapDelete("/api/data/tables/{tableName}/{id}", async (DynamicDataService dataService, string tableName, string id) =>
{
    var success = await dataService.DeleteAsync(tableName, id);
    if (!success)
        return Results.NotFound();
    
    return Results.Ok(new { message = "Deleted successfully" });
})
.RequireAuthorization()
.WithTags("SQL Server - Dynamic Tables")
.WithName("DeleteTableRecord");

// MongoDB dynamic data access
app.MapGet("/api/data/collections", async (DynamicDataService dataService) =>
{
    var collections = await dataService.GetCollectionNamesAsync();
    return Results.Ok(collections);
})
.RequireAuthorization()
.WithTags("MongoDB - Dynamic Collections")
.WithName("GetCollectionNames");

app.MapGet("/api/data/collections/{collectionName}", async (DynamicDataService dataService, string collectionName, string? filter, string? sort, int? limit) =>
{
    var data = await dataService.QueryMongoAsync(collectionName, filter, sort, limit);
    return Results.Ok(data);
})
.RequireAuthorization()
.WithTags("MongoDB - Dynamic Collections")
.WithName("QueryCollection");

app.MapGet("/api/data/collections/{collectionName}/{id}", async (DynamicDataService dataService, string collectionName, string id) =>
{
    var data = await dataService.GetMongoByIdAsync(collectionName, id);
    if (data == null)
        return Results.NotFound();
    
    return Results.Ok(data);
})
.RequireAuthorization()
.WithTags("MongoDB - Dynamic Collections")
.WithName("GetCollectionRecord");

app.MapPost("/api/data/collections/{collectionName}", async (DynamicDataService dataService, string collectionName, [FromBody] Dictionary<string, object> data) =>
{
    var id = await dataService.InsertMongoAsync(collectionName, data);
    return Results.Created($"/api/data/collections/{collectionName}/{id}", new { id });
})
.RequireAuthorization()
.WithTags("MongoDB - Dynamic Collections")
.WithName("InsertCollectionRecord");

app.MapPut("/api/data/collections/{collectionName}/{id}", async (DynamicDataService dataService, string collectionName, string id, [FromBody] Dictionary<string, object> data) =>
{
    var success = await dataService.UpdateMongoAsync(collectionName, id, data);
    if (!success)
        return Results.NotFound();
    
    return Results.Ok(new { message = "Updated successfully" });
})
.RequireAuthorization()
.WithTags("MongoDB - Dynamic Collections")
.WithName("UpdateCollectionRecord");

app.MapDelete("/api/data/collections/{collectionName}/{id}", async (DynamicDataService dataService, string collectionName, string id) =>
{
    var success = await dataService.DeleteMongoAsync(collectionName, id);
    if (!success)
        return Results.NotFound();
    
    return Results.Ok(new { message = "Deleted successfully" });
})
.RequireAuthorization()
.WithTags("MongoDB - Dynamic Collections")
.WithName("DeleteCollectionRecord");

// Orders endpoints (SQL Server)
app.MapGet("/api/orders", async (OrdersRepository ordersRepository, HttpContext context) =>
{
    var orders = await ordersRepository.GetOrdersAsync();
    return Results.Ok(orders);
})
.RequireAuthorization()
.WithTags("SQL Server - Orders")
.WithName("GetOrders");

app.MapPost("/api/orders", async (OrdersRepository ordersRepository, [FromBody] CreateOrderRequest request) =>
{
    if (string.IsNullOrEmpty(request.Code))
        return Results.BadRequest(new { error = "Code is required" });
    
    var order = await ordersRepository.CreateOrderAsync(request.Code, request.Amount);
    return Results.Created($"/api/orders/{order.Id}", order);
})
.RequireAuthorization()
.WithTags("SQL Server - Orders")
.WithName("CreateOrder");

app.MapGet("/api/orders/{id}", async (OrdersRepository ordersRepository, string id) =>
{
    var order = await ordersRepository.GetOrderByIdAsync(id);
    if (order == null)
        return Results.NotFound();
    
    return Results.Ok(order);
})
.RequireAuthorization()
.WithTags("SQL Server - Orders")
.WithName("GetOrderById");

// Events endpoints (MongoDB)
app.MapGet("/api/events", async (EventsRepository eventsRepository, string? type) =>
{
    var events = await eventsRepository.GetEventsAsync(type);
    return Results.Ok(events);
})
.RequireAuthorization()
.WithTags("MongoDB - Events")
.WithName("GetEvents");

app.MapPost("/api/events", async (EventsRepository eventsRepository, [FromBody] CreateEventRequest request) =>
{
    if (string.IsNullOrEmpty(request.Type))
        return Results.BadRequest(new { error = "Type is required" });
    
    var evt = await eventsRepository.CreateEventAsync(request.Type, request.Payload);
    return Results.Created($"/api/events/{evt.Id}", evt);
})
.RequireAuthorization()
.WithTags("MongoDB - Events")
.WithName("CreateEvent");

// Initialize database and seed data on first run
using (var scope = app.Services.CreateScope())
{
    var catalogDbContext = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
    var catalogRepository = scope.ServiceProvider.GetRequiredService<ICatalogRepository>();
    var provisioningService = scope.ServiceProvider.GetRequiredService<ProvisioningService>();
    var ordersRepository = scope.ServiceProvider.GetRequiredService<OrdersRepository>();
    var eventsRepository = scope.ServiceProvider.GetRequiredService<EventsRepository>();
    
    try
    {
        // Ensure database is created
        await catalogDbContext.Database.EnsureCreatedAsync();
        app.Logger.LogInformation("Catalog database initialized successfully");
        
        // Check if catalog is empty
        var existingTenants = await catalogRepository.GetAllTenantsAsync();
        if (!existingTenants.Any())
        {
            // Create demo tenant
            var demoTenantId = await provisioningService.CreateTenantAsync("demo-tenant");
            
            // Create sample data (this would normally be done in a separate service)
            // For now, we'll just log that seeding is complete
            app.Logger.LogInformation("Demo tenant created with ID: {TenantId}", demoTenantId);
        }
    }
    catch (Exception ex)
    {
        app.Logger.LogError(ex, "Failed to initialize database");
        throw;
    }
}

app.Run();

// Request/Response models
public record DevTokenRequest(string TenantId);
public record CreateTenantRequest(string Name, SchemaDefinition? SchemaDefinition = null);
public record CreateOrderRequest(string Code, decimal Amount);
public record CreateEventRequest(string Type, object Payload);
