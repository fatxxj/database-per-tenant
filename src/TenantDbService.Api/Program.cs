using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using System.Diagnostics.Metrics;
using System.Text;
using TenantDbService.Api.Auth;
using TenantDbService.Api.Catalog;
using TenantDbService.Api.Common;
using TenantDbService.Api.Data.Mongo;
using TenantDbService.Api.Data.Sql;
using TenantDbService.Api.Endpoints;
using TenantDbService.Api.Features.Events;
using TenantDbService.Api.Features.Orders;
using TenantDbService.Api.Middleware;
using TenantDbService.Api.Provisioning;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddHttpContextAccessor();

builder.Services.Configure<JwtSettings>(builder.Configuration.GetSection("Jwt"));
builder.Services.Configure<ConnectionSettings>(builder.Configuration.GetSection("ConnectionStrings"));
builder.Services.Configure<SqlServerSettings>(builder.Configuration.GetSection("SqlServer"));
builder.Services.Configure<MongoSettings>(builder.Configuration.GetSection("Mongo"));

builder.Services.AddDbContext<CatalogDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("Catalog"), 
        sqlServerOptionsAction: sqlOptions =>
        {
            sqlOptions.EnableRetryOnFailure(
                maxRetryCount: 5,
                maxRetryDelay: TimeSpan.FromSeconds(30),
                errorNumbersToAdd: null);
        }));

builder.Services.AddScoped<ICatalogRepository, CatalogRepository>();
builder.Services.AddScoped<ProvisioningService>();
builder.Services.AddScoped<OrdersRepository>();
builder.Services.AddScoped<EventsRepository>();
builder.Services.AddScoped<DynamicSchemaService>();
builder.Services.AddScoped<DynamicDataService>();
builder.Services.AddScoped<SqlConnectionFactory>();
builder.Services.AddScoped<IMongoDbFactory, MongoDbFactory>();
builder.Services.AddScoped<JwtExtensions>();

builder.Services.AddMemoryCache();

builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowSpecificOrigins", policy =>
    {
        var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() 
            ?? new[] { "*" };
        
        if (allowedOrigins.Contains("*"))
        {
            policy.AllowAnyOrigin()
                  .AllowAnyHeader()
                  .AllowAnyMethod();
        }
        else
        {
            policy.WithOrigins(allowedOrigins)
                  .AllowAnyHeader()
                  .AllowAnyMethod()
                  .AllowCredentials();
        }
    });
});

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

builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo 
    { 
        Title = "TenantDbService API", 
        Version = "v1",
        Description = "Multi-tenant database microservice with per-tenant isolation"
    });
    
    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Description = "JWT Authorization header using the Bearer scheme. Enter 'Bearer' [space] and then your token.",
        Name = "Authorization",
        In = ParameterLocation.Header,
        Type = SecuritySchemeType.Http,
        Scheme = "Bearer",
        BearerFormat = "JWT"
    });
    
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
});

var meter = new Meter("TenantDbService");
var counter = meter.CreateCounter<long>("requests_total");
var histogram = meter.CreateHistogram<double>("request_duration_ms");

builder.Services.AddSingleton(meter);
builder.Services.AddSingleton(counter);
builder.Services.AddSingleton(histogram);

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "TenantDbService API v1");
        c.RoutePrefix = "swagger";
        c.DisplayRequestDuration();
        c.EnablePersistAuthorization();
        c.EnableDeepLinking();
        c.EnableFilter();
    });
}

app.UseMiddleware<GlobalExceptionMiddleware>();
app.UseMiddleware<RequestLoggingMiddleware>();

if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseCors("AllowSpecificOrigins");
app.UseMiddleware<TenantResolutionMiddleware>();
app.UseAuthentication();
app.UseAuthorization();

// Map all endpoints
app.MapHealthEndpoints();
app.MapAuthEndpoints();
app.MapTenantEndpoints();
app.MapSchemaEndpoints();
app.MapDynamicDataEndpoints();
app.MapMongoDataEndpoints();
app.MapOrderEndpoints();
app.MapEventEndpoints();

using (var scope = app.Services.CreateScope())
{
    var catalogDbContext = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
    var catalogRepository = scope.ServiceProvider.GetRequiredService<ICatalogRepository>();
    var provisioningService = scope.ServiceProvider.GetRequiredService<ProvisioningService>();
    
    try
    {
        await catalogDbContext.Database.EnsureCreatedAsync();
        app.Logger.LogInformation("Catalog database initialized successfully");
        
        var existingTenants = await catalogRepository.GetAllTenantsAsync();
        if (!existingTenants.Any())
        {
            var demoTenantId = await provisioningService.CreateTenantAsync("demo-tenant", DatabaseType.Both);
            app.Logger.LogInformation("Demo tenant created with ID: {TenantId} using database type: Both", demoTenantId);
        }
    }
    catch (Exception ex)
    {
        app.Logger.LogError(ex, "Failed to initialize database");
        throw;
    }
}

app.Run();
