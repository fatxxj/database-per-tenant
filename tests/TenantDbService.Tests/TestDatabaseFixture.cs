using Dapper;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TenantDbService.Api.Catalog;
using TenantDbService.Api.Common;
using TenantDbService.Api.Data.Mongo;
using TenantDbService.Api.Data.Sql;
using TenantDbService.Api.Provisioning;

namespace TenantDbService.Tests;

public class TestDatabaseFixture : IDisposable
{
    public IServiceProvider ServiceProvider { get; private set; }
    private readonly string _testCatalogDatabaseName;
    private readonly string _masterConnectionString;

    public TestDatabaseFixture()
    {
        _testCatalogDatabaseName = $"test_catalog_{Guid.NewGuid():N}";
        
        var testProjectDirectory = Path.GetDirectoryName(typeof(TestDatabaseFixture).Assembly.Location) 
            ?? Directory.GetCurrentDirectory();
        
        var configuration = new ConfigurationBuilder()
            .SetBasePath(testProjectDirectory)
            .AddJsonFile("appsettings.json", optional: true)
            .AddJsonFile("appsettings.Development.json", optional: true)
            .AddEnvironmentVariables()
            .Build();

        var services = new ServiceCollection();

        services.AddSingleton<IConfiguration>(configuration);

        services.AddLogging(builder =>
        {
            builder.AddConsole();
            builder.SetMinimumLevel(LogLevel.Warning);
        });

        var baseConnectionString = configuration.GetConnectionString("Catalog") 
            ?? "Server=localhost,1433;User Id=sa;Password=P@ssw0rd!;TrustServerCertificate=True;";
        
        var builder = new SqlConnectionStringBuilder(baseConnectionString);
        builder.InitialCatalog = "master";
        _masterConnectionString = builder.ConnectionString;
        
        builder.InitialCatalog = _testCatalogDatabaseName;
        var catalogConnectionString = builder.ConnectionString;
        
        services.AddDbContext<CatalogDbContext>(options =>
            options.UseSqlServer(catalogConnectionString));

        services.AddMemoryCache();

        services.AddScoped<ICatalogRepository, CatalogRepository>();

        services.Configure<SqlServerSettings>(options =>
        {
            options.Template = configuration["SqlServer:Template"] 
                ?? "Server=localhost,1433;Database=tenant_{TENANTID};User Id=sa;Password=P@ssw0rd!;TrustServerCertificate=True;";
        });

        services.Configure<MongoSettings>(options =>
        {
            var mongoTemplate = configuration["Mongo:Template"] ?? "mongodb://localhost:27017";
            if (mongoTemplate.Contains("mongodb:27017") || mongoTemplate.Contains("@mongodb:"))
            {
                mongoTemplate = "mongodb://localhost:27017";
            }
            options.Template = mongoTemplate;
            options.DatabaseTemplate = configuration["Mongo:DatabaseTemplate"] 
                ?? "tenant_{TENANTID}";
        });

        services.AddScoped<SqlConnectionFactory>();

        services.AddScoped<IMongoDbFactory, MongoDbFactory>();

        services.AddScoped<DynamicSchemaService>();

        services.AddScoped<DynamicDataService>();

        services.AddScoped<ProvisioningService>();

        services.AddSingleton<IHttpContextAccessor, TestHttpContextAccessor>();

        ServiceProvider = services.BuildServiceProvider();

        CreateTestCatalogDatabase();
        
        using var scope = ServiceProvider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
        context.Database.EnsureCreated();
    }

    private void CreateTestCatalogDatabase()
    {
        try
        {
            using var masterConnection = new SqlConnection(_masterConnectionString);
            masterConnection.Open();
            
            var createDbQuery = $@"
                IF NOT EXISTS (SELECT * FROM sys.databases WHERE name = '{_testCatalogDatabaseName}')
                BEGIN
                    CREATE DATABASE [{_testCatalogDatabaseName}];
                END";
            
            masterConnection.Execute(createDbQuery);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to create test catalog database '{_testCatalogDatabaseName}'. Ensure SQL Server is running and accessible.", ex);
        }
    }

    public void Dispose()
    {
        // Dispose service provider first
        if (ServiceProvider is IDisposable disposable)
        {
            disposable.Dispose();
        }

        DropTestCatalogDatabase();
    }

    private void DropTestCatalogDatabase()
    {
        try
        {
            using var masterConnection = new SqlConnection(_masterConnectionString);
            masterConnection.Open();
            
            var setSingleUserQuery = $@"
                IF EXISTS (SELECT * FROM sys.databases WHERE name = '{_testCatalogDatabaseName}')
                BEGIN
                    ALTER DATABASE [{_testCatalogDatabaseName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
                END";
            
            masterConnection.Execute(setSingleUserQuery);
            
            var dropDbQuery = $@"
                IF EXISTS (SELECT * FROM sys.databases WHERE name = '{_testCatalogDatabaseName}')
                BEGIN
                    DROP DATABASE [{_testCatalogDatabaseName}];
                END";
            
            masterConnection.Execute(dropDbQuery);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Warning: Failed to drop test catalog database '{_testCatalogDatabaseName}': {ex.Message}");
        }
    }
}

