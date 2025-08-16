using TenantDbService.Api.Catalog.Entities;
using TenantDbService.Api.Common;

namespace TenantDbService.Api.Catalog;

public interface ICatalogRepository
{
    Task<TenantConnections?> GetConnectionsAsync(string tenantId);
    Task<bool> TenantExistsAsync(string tenantId);
    Task CreateTenantAsync(Tenant tenant, TenantConnections connections);
    Task<List<TenantInfo>> GetAllTenantsAsync();
    Task<bool> DisableTenantAsync(string tenantId);
    Task<bool> TenantNameExistsAsync(string name);
    Task<SchemaDefinition?> GetSchemaAsync(string tenantId);
    Task UpdateSchemaAsync(string tenantId, SchemaDefinition schema);
    Task<Tenant?> GetTenantAsync(string tenantId);
}
