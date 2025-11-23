using Microsoft.AspNetCore.Mvc;
using TenantDbService.Api.Common;

namespace TenantDbService.Api.Endpoints;

public static class MongoDataEndpoints
{
    public static void MapMongoDataEndpoints(this WebApplication app)
    {
        app.MapGet("/api/data/collections", async (DynamicDataService dataService) =>
        {
            var collections = await dataService.GetCollectionNamesAsync();
            return Results.Ok(collections);
        })
        .RequireAuthorization()
        .WithTags("MongoDB - Dynamic Collections")
        .WithName("GetCollectionNames");

        app.MapGet("/api/data/collections/{collectionName}", async (DynamicDataService dataService, string collectionName, string? filter, string? sort, int? limit) =>
        {
            var data = await dataService.QueryMongoAsync(collectionName, filter, sort, limit);
            return Results.Ok(data);
        })
        .RequireAuthorization()
        .WithTags("MongoDB - Dynamic Collections")
        .WithName("QueryCollection");

        app.MapGet("/api/data/collections/{collectionName}/{id}", async (DynamicDataService dataService, string collectionName, string id) =>
        {
            var data = await dataService.GetMongoByIdAsync(collectionName, id);
            if (data == null)
                return Results.NotFound();
            
            return Results.Ok(data);
        })
        .RequireAuthorization()
        .WithTags("MongoDB - Dynamic Collections")
        .WithName("GetCollectionRecord");

        app.MapPost("/api/data/collections/{collectionName}", async (DynamicDataService dataService, string collectionName, [FromBody] Dictionary<string, object> data) =>
        {
            var id = await dataService.InsertMongoAsync(collectionName, data);
            return Results.Created($"/api/data/collections/{collectionName}/{id}", new { id });
        })
        .RequireAuthorization()
        .WithTags("MongoDB - Dynamic Collections")
        .WithName("InsertCollectionRecord");

        app.MapPut("/api/data/collections/{collectionName}/{id}", async (DynamicDataService dataService, string collectionName, string id, [FromBody] Dictionary<string, object> data) =>
        {
            var success = await dataService.UpdateMongoAsync(collectionName, id, data);
            if (!success)
                return Results.NotFound();
            
            return Results.Ok(new { message = "Updated successfully" });
        })
        .RequireAuthorization()
        .WithTags("MongoDB - Dynamic Collections")
        .WithName("UpdateCollectionRecord");

        app.MapDelete("/api/data/collections/{collectionName}/{id}", async (DynamicDataService dataService, string collectionName, string id) =>
        {
            var success = await dataService.DeleteMongoAsync(collectionName, id);
            if (!success)
                return Results.NotFound();
            
            return Results.Ok(new { message = "Deleted successfully" });
        })
        .RequireAuthorization()
        .WithTags("MongoDB - Dynamic Collections")
        .WithName("DeleteCollectionRecord");
    }
}

