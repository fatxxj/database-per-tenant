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
    private readonly CatalogRepository _catalogRepository;
    private readonly SqlConnectionFactory _sqlFactory;
    private readonly MongoDbFactory _mongoFactory;
    private readonly ILogger<ProvisioningService> _logger;
    private readonly SqlServerSettings _sqlSettings;
    private readonly MongoSettings _mongoSettings;

    public ProvisioningService(
        CatalogRepository catalogRepository,
        SqlConnectionFactory sqlFactory,
        MongoDbFactory mongoFactory,
        ILogger<ProvisioningService> logger,
        IOptions<SqlServerSettings> sqlSettings,
        IOptions<MongoSettings> mongoSettings)
    {
        _catalogRepository = catalogRepository;
        _sqlFactory = sqlFactory;
        _mongoFactory = mongoFactory;
        _logger = logger;
        _sqlSettings = sqlSettings.Value;
        _mongoSettings = mongoSettings.Value;
    }

    public async Task<string> CreateTenantAsync(string name)
    {
        if (string.IsNullOrEmpty(name))
            throw new ArgumentException("Tenant name is required", nameof(name));

        // Check if tenant name already exists
        if (await _catalogRepository.TenantNameExistsAsync(name))
            throw new InvalidOperationException($"Tenant with name '{name}' already exists");

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
            CreatedAt = DateTime.UtcNow
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

        // Store in catalog
        await _catalogRepository.CreateTenantAsync(tenant, connections);

        _logger.LogInformation("Successfully created tenant: {TenantId} with name: {TenantName}", tenantId, name);
        
        return tenantId;
    }

    public async Task<bool> DisableTenantAsync(string tenantId)
    {
        return await _catalogRepository.DisableTenantAsync(tenantId);
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

            // Connect to tenant database and create schema
            using var tenantConnection = new Microsoft.Data.SqlClient.SqlConnection(_sqlSettings.Template.Replace("{TENANTID}", tenantId));
            await tenantConnection.OpenAsync();

            // Create Orders table
            var createOrdersTable = @"
                IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'Orders')
                BEGIN
                    CREATE TABLE Orders (
                        Id NVARCHAR(50) PRIMARY KEY,
                        Code NVARCHAR(100) NOT NULL,
                        Amount DECIMAL(18,2) NOT NULL,
                        CreatedAt DATETIME2 NOT NULL DEFAULT GETUTCDATE()
                    )
                END";

            await tenantConnection.ExecuteAsync(createOrdersTable);

            // Create indexes
            var createIndexes = @"
                IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_Orders_CreatedAt')
                BEGIN
                    CREATE INDEX IX_Orders_CreatedAt ON Orders (CreatedAt DESC)
                END
                
                IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_Orders_Code')
                BEGIN
                    CREATE INDEX IX_Orders_Code ON Orders (Code)
                END";

            await tenantConnection.ExecuteAsync(createIndexes);

            _logger.LogInformation("SQL Server schema created for tenant: {TenantId}", tenantId);
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

            // Create events collection with index
            var eventsCollection = database.GetCollection<MongoDB.Bson.BsonDocument>("events");
            
            // Create index on type and created_at
            var indexKeysDefinition = Builders<MongoDB.Bson.BsonDocument>.IndexKeys
                .Ascending("type")
                .Descending("created_at");
            
            var indexModel = new CreateIndexModel<MongoDB.Bson.BsonDocument>(indexKeysDefinition);
            await eventsCollection.Indexes.CreateOneAsync(indexModel);

            _logger.LogInformation("MongoDB database and indexes created for tenant: {TenantId}", tenantId);
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
