using Microsoft.AspNetCore.Mvc;
using TenantDbService.Api.Catalog;
using TenantDbService.Api.Common;
using TenantDbService.Api.Provisioning;

namespace TenantDbService.Api.Endpoints;

public static class SchemaEndpoints
{
    public static void MapSchemaEndpoints(this WebApplication app)
    {
        app.MapPost("/tenants/{tenantId}/schema", async (ProvisioningService provisioningService, string tenantId, [FromBody] UpdateSchemaRequest request) =>
        {
            try
            {
                var schemaDefinition = System.Text.Json.JsonSerializer.Deserialize<SchemaDefinition>(request.SchemaDefinition);
                if (schemaDefinition == null)
                    return Results.BadRequest(new { error = Constants.ErrorMessages.InvalidSchemaDefinition });
                
                await provisioningService.UpdateSchemaAsync(tenantId, schemaDefinition);
                return Results.Ok(new { message = "Schema updated successfully" });
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        })
        .RequireAuthorization()
        .WithName("UpdateTenantSchema")
        .WithTags("Schema Management");

        app.MapGet("/tenants/{tenantId}/schema", async (ICatalogRepository catalogRepository, string tenantId) =>
        {
            var schema = await catalogRepository.GetSchemaAsync(tenantId);
            if (schema == null)
                return Results.NotFound(new { error = "Schema not found" });
            
            return Results.Ok(schema);
        })
        .RequireAuthorization()
        .WithName("GetTenantSchema")
        .WithTags("Schema Management");

        app.MapPost("/schema/validate", (DynamicSchemaService schemaService, [FromBody] SchemaValidationRequest request) =>
        {
            var validation = schemaService.ValidateSchema(request.SchemaDefinition);
            return Results.Ok(new SchemaValidationResponse(validation.IsValid, validation.Errors));
        })
        .WithName("ValidateSchema")
        .WithTags("Schema Management");
    }
}

