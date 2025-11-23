using Microsoft.AspNetCore.Mvc;
using TenantDbService.Api.Catalog;
using TenantDbService.Api.Common;
using TenantDbService.Api.Provisioning;

namespace TenantDbService.Api.Endpoints;

public static class TenantEndpoints
{
    public static void MapTenantEndpoints(this WebApplication app)
    {
        app.MapPost("/tenants", async (ProvisioningService provisioningService, [FromBody] CreateTenantRequest request) =>
        {
            if (string.IsNullOrEmpty(request.Name))
                return Results.BadRequest(new { error = Constants.ErrorMessages.TenantNameRequired });
            
            var tenantId = await provisioningService.CreateTenantAsync(request.Name, request.DatabaseType, request.SchemaDefinition);
            return Results.Ok(new { tenantId, databaseType = request.DatabaseType.ToString() });
        })
        .WithName("CreateTenant")
        .WithTags("Tenant Management");

        app.MapGet("/tenants", async (ICatalogRepository catalogRepository) =>
        {
            var tenants = await catalogRepository.GetAllTenantsAsync();
            return Results.Ok(tenants);
        })
        .WithName("ListTenants")
        .WithTags("Tenant Management");
    }
}

public record CreateTenantRequest(string Name, DatabaseType DatabaseType = DatabaseType.Both, SchemaDefinition? SchemaDefinition = null);

