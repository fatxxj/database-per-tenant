using Microsoft.Extensions.Options;
using MongoDB.Driver;
using TenantDbService.Api.Catalog.Entities;
using TenantDbService.Api.Common;

namespace TenantDbService.Api.Data.Mongo;

public class MongoDbFactory : IMongoDbFactory
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
        if (context?.Items.TryGetValue(Constants.HttpItems.TenantContext, out var tenantCtxObj) != true || 
            tenantCtxObj is not TenantContext tenantCtx)
        {
            throw new InvalidOperationException(Constants.ErrorMessages.TenantContextNotFound);
        }

        if (string.IsNullOrEmpty(tenantCtx.Connections.MongoDbConnectionString) || 
            string.IsNullOrEmpty(tenantCtx.Connections.MongoDbDatabaseName))
        {
            throw new InvalidOperationException($"Tenant '{tenantCtx.Tenant.Name}' (ID: {tenantCtx.Tenant.Id}) does not have MongoDB database provisioned. This tenant uses {tenantCtx.Tenant.DatabaseType} database type.");
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
