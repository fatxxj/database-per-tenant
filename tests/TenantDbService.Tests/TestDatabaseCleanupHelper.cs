using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using MongoDB.Driver;
using TenantDbService.Api.Catalog;
using TenantDbService.Api.Common;
using TenantDbService.Api.Data.Mongo;

namespace TenantDbService.Tests;

public static class TestDatabaseCleanupHelper
{
    public static async Task CleanupTenantAsync(
        IServiceProvider serviceProvider,
        string tenantId,
        DatabaseType databaseType)
    {
        if (string.IsNullOrEmpty(tenantId))
            return;

        try
        {
            var catalogRepository = serviceProvider.GetRequiredService<ICatalogRepository>();
            var tenant = await catalogRepository.GetTenantAsync(tenantId);
            var connections = await catalogRepository.GetConnectionsAsync(tenantId);

            if ((databaseType == DatabaseType.SqlServer || databaseType == DatabaseType.Both) 
                && !string.IsNullOrEmpty(connections?.SqlServerConnectionString))
            {
                await DropSqlServerDatabaseAsync(serviceProvider, tenantId);
            }

            if ((databaseType == DatabaseType.MongoDb || databaseType == DatabaseType.Both) 
                && !string.IsNullOrEmpty(connections?.MongoDbDatabaseName))
            {
                await DropMongoDatabaseAsync(serviceProvider, connections.MongoDbDatabaseName);
            }

            await DeleteTenantFromCatalogAsync(serviceProvider, tenantId);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Warning: Failed to cleanup tenant {tenantId}: {ex.Message}");
        }
    }

    private static async Task DropSqlServerDatabaseAsync(IServiceProvider serviceProvider, string tenantId)
    {
        try
        {
            var sqlSettings = serviceProvider.GetRequiredService<IOptions<SqlServerSettings>>().Value;
            
            var masterConnectionString = sqlSettings.Template.Replace("Database=tenant_{TENANTID}", "Database=master");
            
            var databaseName = $"tenant_{tenantId}";
            
            using var masterConnection = new SqlConnection(masterConnectionString);
            await masterConnection.OpenAsync();

            var setSingleUserQuery = $@"
                IF EXISTS (SELECT * FROM sys.databases WHERE name = '{databaseName}')
                BEGIN
                    ALTER DATABASE [{databaseName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
                END";
            
            await masterConnection.ExecuteAsync(setSingleUserQuery);

            var dropDbQuery = $@"
                IF EXISTS (SELECT * FROM sys.databases WHERE name = '{databaseName}')
                BEGIN
                    DROP DATABASE [{databaseName}];
                END";
            
            await masterConnection.ExecuteAsync(dropDbQuery);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Warning: Failed to drop SQL Server database tenant_{tenantId}: {ex.Message}");
            throw;
        }
    }

    private static async Task DropMongoDatabaseAsync(IServiceProvider serviceProvider, string databaseName)
    {
        try
        {
            var mongoSettings = serviceProvider.GetRequiredService<IOptions<MongoSettings>>().Value;
            var client = new MongoClient(mongoSettings.Template);
            await client.DropDatabaseAsync(databaseName);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Warning: Failed to drop MongoDB database {databaseName}: {ex.Message}");
            throw;
        }
    }

    private static async Task DeleteTenantFromCatalogAsync(IServiceProvider serviceProvider, string tenantId)
    {
        try
        {
            using var scope = serviceProvider.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
            
            var tenant = await context.Tenants.FindAsync(tenantId);
            if (tenant != null)
            {
                context.Tenants.Remove(tenant);
                await context.SaveChangesAsync();
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Warning: Failed to delete tenant {tenantId} from catalog: {ex.Message}");
        }
    }
}

