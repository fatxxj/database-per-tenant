using MongoDB.Driver;

namespace TenantDbService.Api.Data.Mongo;

public interface IMongoDbFactory
{
    Task<IMongoDatabase> GetDatabaseAsync();
    string BuildConnectionString(string tenantId);
    string BuildDatabaseName(string tenantId);
}
