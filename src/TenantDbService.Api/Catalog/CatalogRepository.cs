using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using TenantDbService.Api.Catalog.Entities;
using TenantDbService.Api.Common;

namespace TenantDbService.Api.Catalog;

public class CatalogRepository
{
    private readonly CatalogDbContext _context;
    private readonly IMemoryCache _cache;
    private readonly ILogger<CatalogRepository> _logger;
    private const string ConnectionCacheKey = "tenant_connections_{0}";
    private const int CacheTtlMinutes = 5;

    public CatalogRepository(CatalogDbContext context, IMemoryCache cache, ILogger<CatalogRepository> logger)
    {
        _context = context;
        _cache = cache;
        _logger = logger;
    }

    public async Task<TenantConnections?> GetConnectionsAsync(string tenantId)
    {
        var cacheKey = string.Format(ConnectionCacheKey, tenantId);
        
        if (_cache.TryGetValue(cacheKey, out TenantConnections? cachedConnections))
        {
            _logger.LogDebug("Cache hit for tenant connections: {TenantId}", tenantId);
            return cachedConnections;
        }

        var connections = await _context.TenantConnections
            .FirstOrDefaultAsync(tc => tc.TenantId == tenantId && tc.Tenant!.Status == "active");

        if (connections != null)
        {
            var cacheOptions = new MemoryCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(CacheTtlMinutes)
            };
            _cache.Set(cacheKey, connections, cacheOptions);
            _logger.LogDebug("Cached tenant connections: {TenantId}", tenantId);
        }

        return connections;
    }

    public async Task<bool> TenantExistsAsync(string tenantId)
    {
        return await _context.Tenants
            .AnyAsync(t => t.Id == tenantId && t.Status == "active");
    }

    public async Task CreateTenantAsync(Tenant tenant, TenantConnections connections)
    {
        using var transaction = await _context.Database.BeginTransactionAsync();
        
        try
        {
            _context.Tenants.Add(tenant);
            _context.TenantConnections.Add(connections);
            
            await _context.SaveChangesAsync();
            await transaction.CommitAsync();
            
            // Invalidate cache
            var cacheKey = string.Format(ConnectionCacheKey, tenant.Id);
            _cache.Remove(cacheKey);
            
            _logger.LogInformation("Created tenant: {TenantId} with name: {TenantName}", tenant.Id, tenant.Name);
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    public async Task<List<TenantInfo>> GetAllTenantsAsync()
    {
        return await _context.Tenants
            .Where(t => t.Status == "active")
            .Select(t => new TenantInfo(t.Id, t.Name, t.Status, t.CreatedAt))
            .ToListAsync();
    }

    public async Task<bool> DisableTenantAsync(string tenantId)
    {
        var tenant = await _context.Tenants.FindAsync(tenantId);
        if (tenant == null)
            return false;

        tenant.Status = "disabled";
        await _context.SaveChangesAsync();
        
        // Invalidate cache
        var cacheKey = string.Format(ConnectionCacheKey, tenantId);
        _cache.Remove(cacheKey);
        
        _logger.LogInformation("Disabled tenant: {TenantId}", tenantId);
        return true;
    }

    public async Task<bool> TenantNameExistsAsync(string name)
    {
        return await _context.Tenants
            .AnyAsync(t => t.Name == name && t.Status == "active");
    }
}
