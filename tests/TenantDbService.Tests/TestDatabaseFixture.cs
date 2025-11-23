using Microsoft.AspNetCore.Http;
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

    public TestDatabaseFixture()
    {
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

        var catalogConnectionString = configuration.GetConnectionString("Catalog") 
            ?? "Server=localhost,1433;Database=test_catalog;User Id=sa;Password=P@ssw0rd!;TrustServerCertificate=True;";
        
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

        using var scope = ServiceProvider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
        context.Database.EnsureCreated();
    }

    public void Dispose()
    {
        if (ServiceProvider is IDisposable disposable)
        {
            disposable.Dispose();
        }
    }
}

