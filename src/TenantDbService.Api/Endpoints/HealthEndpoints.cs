using Microsoft.EntityFrameworkCore;
using TenantDbService.Api.Catalog;
using TenantDbService.Api.Common;
using TenantDbService.Api.Data.Mongo;
using TenantDbService.Api.Data.Sql;

namespace TenantDbService.Api.Endpoints;

public static class HealthEndpoints
{
    public static void MapHealthEndpoints(this WebApplication app)
    {
        app.MapGet("/health/live", () => Results.Ok(new { status = "healthy" }))
            .WithName("HealthLive")
            .WithTags("Health");

        app.MapGet("/health/ready", async (CatalogDbContext catalogDb, HttpContext context) =>
        {
            try
            {
                await catalogDb.Database.CanConnectAsync();
                
                if (context.Items.TryGetValue(Constants.HttpItems.TenantContext, out var tenantCtxObj) && 
                    tenantCtxObj is TenantContext tenantCtx)
                {
                    var sqlFactory = context.RequestServices.GetRequiredService<SqlConnectionFactory>();
                    using var sqlConnection = await sqlFactory.CreateConnectionAsync();
                    await sqlConnection.OpenAsync();
                    
                    var mongoFactory = context.RequestServices.GetRequiredService<IMongoDbFactory>();
                    var mongoDb = await mongoFactory.GetDatabaseAsync();
                    await mongoDb.RunCommandAsync<MongoDB.Bson.BsonDocument>(new MongoDB.Bson.BsonDocument("ping", 1));
                }
                
                return Results.Ok(new { status = "ready" });
            }
            catch
            {
                return Results.StatusCode(503);
            }
        })
        .WithName("HealthReady")
        .WithTags("Health");
    }
}

