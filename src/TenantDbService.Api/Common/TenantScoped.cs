using TenantDbService.Api.Catalog.Entities;

namespace TenantDbService.Api.Common;

public class TenantContext
{
    public string TenantId { get; set; } = string.Empty;
    public TenantConnections Connections { get; set; } = new();
}

public class ConnectionSettings
{
    public string Catalog { get; set; } = string.Empty;
}

public class SqlServerSettings
{
    public string Template { get; set; } = string.Empty;
}

public class MongoSettings
{
    public string Template { get; set; } = string.Empty;
    public string DatabaseTemplate { get; set; } = string.Empty;
}

public record TenantInfo(string Id, string Name, string Status, DateTime CreatedAt);

public record TenantConnectionsInfo(string TenantId, string SqlServerConnectionString, string MongoDbConnectionString, string MongoDbDatabaseName);
