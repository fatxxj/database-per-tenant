using Dapper;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MongoDB.Driver;
using System.Data;
using TenantDbService.Api.Catalog;
using TenantDbService.Api.Catalog.Entities;
using TenantDbService.Api.Data.Mongo;
using TenantDbService.Api.Data.Sql;
using TenantDbService.Api.Common;

namespace TenantDbService.Api.Provisioning;

public class ProvisioningService
{
    private readonly ICatalogRepository _catalogRepository;
    private readonly SqlConnectionFactory _sqlFactory;
    private readonly IMongoDbFactory _mongoFactory;
    private readonly DynamicSchemaService _schemaService;
    private readonly ILogger<ProvisioningService> _logger;
    private readonly SqlServerSettings _sqlSettings;
    private readonly MongoSettings _mongoSettings;

    public ProvisioningService(
        ICatalogRepository catalogRepository,
        SqlConnectionFactory sqlFactory,
        IMongoDbFactory mongoFactory,
        DynamicSchemaService schemaService,
        ILogger<ProvisioningService> logger,
        IOptions<SqlServerSettings> sqlSettings,
        IOptions<MongoSettings> mongoSettings)
    {
        _catalogRepository = catalogRepository;
        _sqlFactory = sqlFactory;
        _mongoFactory = mongoFactory;
        _schemaService = schemaService;
        _logger = logger;
        _sqlSettings = sqlSettings.Value;
        _mongoSettings = mongoSettings.Value;
    }

    public async Task<string> CreateTenantAsync(string name, SchemaDefinition? schemaDefinition = null)
    {
        if (string.IsNullOrEmpty(name))
            throw new ArgumentException("Tenant name is required", nameof(name));

        // Check if tenant name already exists
        if (await _catalogRepository.TenantNameExistsAsync(name))
            throw new InvalidOperationException($"Tenant with name '{name}' already exists");

        // Validate schema if provided
        if (schemaDefinition != null)
        {
            var validation = _schemaService.ValidateSchema(schemaDefinition);
            if (!validation.IsValid)
            {
                throw new ArgumentException($"Invalid schema: {string.Join(", ", validation.Errors)}");
            }
        }

        // Generate tenant ID
        var tenantId = GenerateTenantId();
        
        // Create connection strings
        var sqlConnectionString = _sqlSettings.Template.Replace("{TENANTID}", tenantId);
        var mongoConnectionString = _mongoSettings.Template;
        var mongoDatabaseName = _mongoSettings.DatabaseTemplate.Replace("{TENANTID}", tenantId);

        // Create tenant entities
        var tenant = new Tenant
        {
            Id = tenantId,
            Name = name,
            Status = "active",
            CreatedAt = DateTime.UtcNow,
            SchemaVersion = schemaDefinition?.Version ?? "1.0",
            SchemaDefinition = schemaDefinition != null ? System.Text.Json.JsonSerializer.Serialize(schemaDefinition) : null,
            SchemaUpdatedAt = schemaDefinition != null ? DateTime.UtcNow : null
        };

        var connections = new TenantConnections
        {
            TenantId = tenantId,
            SqlServerConnectionString = sqlConnectionString,
            MongoDbConnectionString = mongoConnectionString,
            MongoDbDatabaseName = mongoDatabaseName,
            CreatedAt = DateTime.UtcNow
        };

        // Provision databases
        await ProvisionSqlServerDatabaseAsync(tenantId);
        await ProvisionMongoDatabaseAsync(tenantId, mongoDatabaseName);

        // Create dynamic schema if provided
        if (schemaDefinition != null)
        {
            await CreateDynamicSchemaAsync(tenantId, schemaDefinition);
        }

        // Store in catalog
        await _catalogRepository.CreateTenantAsync(tenant, connections);

        _logger.LogInformation("Successfully created tenant: {TenantId} with name: {TenantName}", tenantId, name);
        
        return tenantId;
    }

    public async Task<bool> DisableTenantAsync(string tenantId)
    {
        return await _catalogRepository.DisableTenantAsync(tenantId);
    }

    public async Task UpdateSchemaAsync(string tenantId, SchemaDefinition schemaDefinition)
    {
        // Validate schema
        var validation = _schemaService.ValidateSchema(schemaDefinition);
        if (!validation.IsValid)
        {
            throw new ArgumentException($"Invalid schema: {string.Join(", ", validation.Errors)}");
        }

        // Check if tenant exists
        var tenant = await _catalogRepository.GetTenantAsync(tenantId);
        if (tenant == null)
        {
            throw new ArgumentException($"Tenant not found: {tenantId}");
        }

        // Update schema in catalog
        await _catalogRepository.UpdateSchemaAsync(tenantId, schemaDefinition);

        // Apply schema changes to databases
        await ApplySchemaChangesAsync(tenantId, schemaDefinition);

        _logger.LogInformation("Successfully updated schema for tenant: {TenantId}", tenantId);
    }

    private async Task CreateDynamicSchemaAsync(string tenantId, SchemaDefinition schemaDefinition)
    {
        try
        {
            // Create SQL Server schema
            using var sqlConnection = new Microsoft.Data.SqlClient.SqlConnection(_sqlSettings.Template.Replace("{TENANTID}", tenantId));
            await sqlConnection.OpenAsync();
            await _schemaService.CreateSchemaAsync(sqlConnection, schemaDefinition);

            // Create MongoDB collections
            var mongoClient = new MongoClient(_mongoSettings.Template);
            var databaseName = _mongoSettings.DatabaseTemplate.Replace("{TENANTID}", tenantId);
            var database = mongoClient.GetDatabase(databaseName);
            await _schemaService.CreateMongoCollectionsAsync(database, schemaDefinition);

            _logger.LogInformation("Dynamic schema created for tenant: {TenantId}", tenantId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create dynamic schema for tenant: {TenantId}", tenantId);
            throw;
        }
    }

    private async Task ApplySchemaChangesAsync(string tenantId, SchemaDefinition schemaDefinition)
    {
        try
        {
            // Get current schema for comparison (if needed for migrations)
            var currentSchema = await _catalogRepository.GetSchemaAsync(tenantId);
            
            // For now, we'll recreate the schema
            // In a production system, you'd want to implement proper schema migration
            await CreateDynamicSchemaAsync(tenantId, schemaDefinition);

            _logger.LogInformation("Schema changes applied for tenant: {TenantId}", tenantId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to apply schema changes for tenant: {TenantId}", tenantId);
            throw;
        }
    }

    private async Task ProvisionSqlServerDatabaseAsync(string tenantId)
    {
        try
        {
            // Connect to master database to create tenant database
            var masterConnectionString = _sqlSettings.Template.Replace("Database=tenant_{TENANTID}", "Database=master");
            using var masterConnection = new Microsoft.Data.SqlClient.SqlConnection(masterConnectionString);
            await masterConnection.OpenAsync();

            var databaseName = $"tenant_{tenantId}";
            
            // Check if database already exists
            var dbExistsQuery = "SELECT COUNT(*) FROM sys.databases WHERE name = @DatabaseName";
            var dbExists = await masterConnection.ExecuteScalarAsync<int>(dbExistsQuery, new { DatabaseName = databaseName });
            
            if (dbExists == 0)
            {
                // Create database
                var createDbQuery = $"CREATE DATABASE [{databaseName}]";
                await masterConnection.ExecuteAsync(createDbQuery);
                _logger.LogInformation("Created SQL Server database: {DatabaseName}", databaseName);
            }
            else
            {
                _logger.LogInformation("SQL Server database already exists: {DatabaseName}", databaseName);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to provision SQL Server database for tenant: {TenantId}", tenantId);
            throw;
        }
    }

    private async Task ProvisionMongoDatabaseAsync(string tenantId, string databaseName)
    {
        try
        {
            var client = new MongoClient(_mongoSettings.Template);
            var database = client.GetDatabase(databaseName);

            // Test connection
            await database.RunCommandAsync<MongoDB.Bson.BsonDocument>(new MongoDB.Bson.BsonDocument("ping", 1));

            _logger.LogInformation("MongoDB database provisioned for tenant: {TenantId}", tenantId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to provision MongoDB database for tenant: {TenantId}", tenantId);
            throw;
        }
    }

    private static string GenerateTenantId()
    {
        // Generate 12-16 character lowercase alphanumeric ID
        const string chars = "abcdefghijklmnopqrstuvwxyz0123456789";
        var random = new Random();
        var length = random.Next(12, 17);
        
        return new string(Enumerable.Repeat(chars, length)
            .Select(s => s[random.Next(s.Length)]).ToArray());
    }
}
