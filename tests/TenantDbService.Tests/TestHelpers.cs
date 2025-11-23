using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using TenantDbService.Api.Catalog;
using TenantDbService.Api.Catalog.Entities;
using TenantDbService.Api.Common;

namespace TenantDbService.Tests;

public static class TestHelpers
{
    public static async Task<HttpContext> SetupTenantContextAsync(
        IServiceProvider serviceProvider,
        string tenantId)
    {
        var httpContextAccessor = serviceProvider.GetRequiredService<IHttpContextAccessor>();
        
        var catalogRepository = serviceProvider.GetRequiredService<ICatalogRepository>();

        var tenant = await catalogRepository.GetTenantAsync(tenantId);
        if (tenant == null)
        {
            throw new InvalidOperationException($"Tenant {tenantId} not found");
        }

        var connections = await catalogRepository.GetConnectionsAsync(tenantId);
        if (connections == null)
        {
            throw new InvalidOperationException($"Connections for tenant {tenantId} not found");
        }

        var tenantContext = new TenantContext
        {
            TenantId = tenantId,
            Tenant = tenant,
            Connections = connections
        };

        var httpContext = new DefaultHttpContext
        {
            RequestServices = serviceProvider
        };
        httpContext.Items[Constants.HttpItems.TenantContext] = tenantContext;
        httpContext.Items[Constants.HttpItems.CorrelationId] = Guid.NewGuid().ToString();

        httpContextAccessor.HttpContext = httpContext;

        if (httpContextAccessor.HttpContext == null || 
            !httpContextAccessor.HttpContext.Items.ContainsKey(Constants.HttpItems.TenantContext))
        {
            throw new InvalidOperationException("Failed to set HttpContext with tenant context");
        }

        return httpContext;
    }
}

