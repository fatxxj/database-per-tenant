using Microsoft.AspNetCore.Mvc;
using TenantDbService.Api.Features.Events;

namespace TenantDbService.Api.Endpoints;

public static class EventEndpoints
{
    public static void MapEventEndpoints(this WebApplication app)
    {
        app.MapGet("/api/events", async (EventsRepository eventsRepository, string? type) =>
        {
            var events = await eventsRepository.GetEventsAsync(type);
            return Results.Ok(events);
        })
        .RequireAuthorization()
        .WithTags("MongoDB - Events")
        .WithName("GetEvents");

        app.MapPost("/api/events", async (EventsRepository eventsRepository, [FromBody] CreateEventRequest request) =>
        {
            if (string.IsNullOrEmpty(request.Type))
                return Results.BadRequest(new { error = "Type is required" });
            
            var evt = await eventsRepository.CreateEventAsync(request.Type, request.Payload);
            return Results.Created($"/api/events/{evt.Id}", evt);
        })
        .RequireAuthorization()
        .WithTags("MongoDB - Events")
        .WithName("CreateEvent");
    }
}

public record CreateEventRequest(string Type, object Payload);

