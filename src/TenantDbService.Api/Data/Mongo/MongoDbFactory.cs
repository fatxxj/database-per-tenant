using Microsoft.Extensions.Options;
using MongoDB.Driver;
using TenantDbService.Api.Catalog.Entities;
using TenantDbService.Api.Common;

namespace TenantDbService.Api.Data.Mongo;

public class MongoDbFactory
{
    private readonly MongoSettings _settings;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public MongoDbFactory(IOptions<MongoSettings> settings, IHttpContextAccessor httpContextAccessor)
    {
        _settings = settings.Value;
        _httpContextAccessor = httpContextAccessor;
    }

    public async Task<IMongoDatabase> GetDatabaseAsync()
    {
        var context = _httpContextAccessor.HttpContext;
        if (context?.Items.TryGetValue("tenant.ctx", out var tenantCtxObj) != true || 
            tenantCtxObj is not TenantContext tenantCtx)
        {
            throw new InvalidOperationException("Tenant context not found");
        }

        var client = new MongoClient(tenantCtx.Connections.MongoDbConnectionString);
        var database = client.GetDatabase(tenantCtx.Connections.MongoDbDatabaseName);
        
        return database;
    }

    public string BuildConnectionString(string tenantId)
    {
        return _settings.Template;
    }

    public string BuildDatabaseName(string tenantId)
    {
        return _settings.DatabaseTemplate.Replace("{TENANTID}", tenantId);
    }
}
