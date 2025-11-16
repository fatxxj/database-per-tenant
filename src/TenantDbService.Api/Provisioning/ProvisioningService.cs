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
            throw new ArgumentException(Constants.ErrorMessages.TenantNameRequired, nameof(name));

        if (await _catalogRepository.TenantNameExistsAsync(name))
            throw new InvalidOperationException(string.Format(Constants.ErrorMessages.TenantAlreadyExists, name));

        if (schemaDefinition != null)
        {
            var validation = _schemaService.ValidateSchema(schemaDefinition);
            if (!validation.IsValid)
            {
                throw new ArgumentException($"{Constants.ErrorMessages.InvalidSchemaDefinition}: {string.Join(", ", validation.Errors)}");
            }
        }

        var tenantId = await GenerateUniqueTenantIdAsync();
        var sqlConnectionString = _sqlSettings.Template.Replace("{TENANTID}", tenantId);
        var mongoConnectionString = _mongoSettings.Template;
        var mongoDatabaseName = _mongoSettings.DatabaseTemplate.Replace("{TENANTID}", tenantId);

        var tenant = new Tenant
        {
            Id = tenantId,
            Name = name,
            Status = Constants.TenantStatus.Active,
            CreatedAt = DateTime.UtcNow,
            SchemaVersion = schemaDefinition?.Version ?? Constants.SchemaDefaults.DefaultVersion,
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

        try
        {
            await _catalogRepository.CreateTenantAsync(tenant, connections);
            
            await ProvisionSqlServerDatabaseAsync(tenantId);
            await ProvisionMongoDatabaseAsync(tenantId, mongoDatabaseName);

            if (schemaDefinition == null)
            {
                await EnsureDefaultSqlSchemaAsync(tenantId);
            }
            else
            {
                await CreateDynamicSchemaAsync(tenantId, schemaDefinition);
            }

            _logger.LogInformation("Successfully created tenant: {TenantId} with name: {TenantName}", tenantId, name);
            return tenantId;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create tenant: {TenantId}. Attempting rollback...", tenantId);
            
            try
            {
                await _catalogRepository.DisableTenantAsync(tenantId);
                await CleanupDatabasesAsync(tenantId, mongoDatabaseName);
            }
            catch (Exception rollbackEx)
            {
                _logger.LogError(rollbackEx, "Rollback failed for tenant: {TenantId}. Manual cleanup may be required", tenantId);
            }
            
            throw;
        }
    }

    public async Task<bool> DisableTenantAsync(string tenantId)
    {
        return await _catalogRepository.DisableTenantAsync(tenantId);
    }

    public async Task UpdateSchemaAsync(string tenantId, SchemaDefinition schemaDefinition)
    {
        var validation = _schemaService.ValidateSchema(schemaDefinition);
        if (!validation.IsValid)
        {
            throw new ArgumentException($"{Constants.ErrorMessages.InvalidSchemaDefinition}: {string.Join(", ", validation.Errors)}");
        }

        var tenant = await _catalogRepository.GetTenantAsync(tenantId);
        if (tenant == null)
        {
            throw new ArgumentException(string.Format(Constants.ErrorMessages.TenantNotFound, tenantId));
        }

        await _catalogRepository.UpdateSchemaAsync(tenantId, schemaDefinition);
        await ApplySchemaChangesAsync(tenantId, schemaDefinition);

        _logger.LogInformation("Successfully updated schema for tenant: {TenantId}", tenantId);
    }

    private async Task CreateDynamicSchemaAsync(string tenantId, SchemaDefinition schemaDefinition)
    {
        try
        {
            using var sqlConnection = new Microsoft.Data.SqlClient.SqlConnection(_sqlSettings.Template.Replace("{TENANTID}", tenantId));
            await sqlConnection.OpenAsync();
            await _schemaService.CreateSchemaAsync(sqlConnection, schemaDefinition);

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
            var currentSchema = await _catalogRepository.GetSchemaAsync(tenantId);
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

    private async Task EnsureDefaultSqlSchemaAsync(string tenantId)
    {
        try
        {
            var tenantConnectionString = _sqlSettings.Template.Replace("{TENANTID}", tenantId);
            using var connection = new Microsoft.Data.SqlClient.SqlConnection(tenantConnectionString);
            await connection.OpenAsync();

            var createOrdersTableSql = @"
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'Orders')
BEGIN
    CREATE TABLE [Orders](
        [Id] NVARCHAR(50) NOT NULL,
        [Code] NVARCHAR(50) NOT NULL,
        [Amount] DECIMAL(18,2) NOT NULL,
        [CreatedAt] DATETIME2 NOT NULL,
        CONSTRAINT [PK_Orders] PRIMARY KEY ([Id])
    );
END";
            await connection.ExecuteAsync(createOrdersTableSql);

            var createIndexesSql = @"
IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_Orders_CreatedAt' AND object_id = OBJECT_ID('Orders'))
BEGIN
    CREATE INDEX [IX_Orders_CreatedAt] ON [Orders] ([CreatedAt] DESC);
END
IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_Orders_Code' AND object_id = OBJECT_ID('Orders'))
BEGIN
    CREATE INDEX [IX_Orders_Code] ON [Orders] ([Code]);
END";
            await connection.ExecuteAsync(createIndexesSql);

            _logger.LogInformation("Ensured default Orders table for tenant: {TenantId}", tenantId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to ensure default SQL schema for tenant: {TenantId}", tenantId);
            throw;
        }
    }

    private async Task<string> GenerateUniqueTenantIdAsync()
    {
        const int maxAttempts = 10;
        for (int attempt = 0; attempt < maxAttempts; attempt++)
        {
            var tenantId = GenerateTenantId();
            
            if (!await _catalogRepository.TenantExistsAsync(tenantId))
            {
                return tenantId;
            }
            
            _logger.LogWarning("Tenant ID collision detected: {TenantId}. Regenerating... (Attempt {Attempt}/{MaxAttempts})", 
                tenantId, attempt + 1, maxAttempts);
        }
        
        throw new InvalidOperationException("Failed to generate unique tenant ID after multiple attempts");
    }

    private static string GenerateTenantId()
    {
        const string chars = "abcdefghijklmnopqrstuvwxyz0123456789";
        const int length = 16;
        var result = new char[length];
        
        using var rng = System.Security.Cryptography.RandomNumberGenerator.Create();
        var bytes = new byte[length];
        rng.GetBytes(bytes);
        
        for (int i = 0; i < length; i++)
        {
            result[i] = chars[bytes[i] % chars.Length];
        }
        
        return new string(result);
    }

    private async Task CleanupDatabasesAsync(string tenantId, string mongoDatabaseName)
    {
        try
        {
            var masterConnectionString = _sqlSettings.Template.Replace("Database=tenant_{TENANTID}", "Database=master");
            using var masterConnection = new Microsoft.Data.SqlClient.SqlConnection(masterConnectionString);
            await masterConnection.OpenAsync();
            
            var databaseName = $"tenant_{tenantId}";
            var dropDbQuery = $"IF EXISTS (SELECT * FROM sys.databases WHERE name = '{databaseName}') DROP DATABASE [{databaseName}]";
            await masterConnection.ExecuteAsync(dropDbQuery);
            
            _logger.LogInformation("Cleaned up SQL Server database: {DatabaseName}", databaseName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to cleanup SQL Server database for tenant: {TenantId}", tenantId);
        }

        try
        {
            var client = new MongoClient(_mongoSettings.Template);
            await client.DropDatabaseAsync(mongoDatabaseName);
            _logger.LogInformation("Cleaned up MongoDB database: {DatabaseName}", mongoDatabaseName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to cleanup MongoDB database for tenant: {TenantId}", tenantId);
        }
    }
}
