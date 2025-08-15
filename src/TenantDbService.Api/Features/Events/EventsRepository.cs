using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using MongoDB.Driver;
using TenantDbService.Api.Data.Mongo;

namespace TenantDbService.Api.Features.Events;

public class Event
{
    public ObjectId Id { get; set; }
    public string Type { get; set; } = string.Empty;
    public object Payload { get; set; } = new();
    public DateTime CreatedAt { get; set; }
}

public class EventsRepository
{
    private readonly MongoDbFactory _mongoFactory;
    private readonly ILogger<EventsRepository> _logger;
    private readonly SemaphoreSlim _indexSemaphore = new(1, 1);

    public EventsRepository(MongoDbFactory mongoFactory, ILogger<EventsRepository> logger)
    {
        _mongoFactory = mongoFactory;
        _logger = logger;
    }

    public async Task<List<Event>> GetEventsAsync(string? type = null)
    {
        var database = await _mongoFactory.GetDatabaseAsync();
        var collection = database.GetCollection<Event>("events");

        // Ensure index exists
        await EnsureIndexesAsync(collection);

        var filter = type != null 
            ? Builders<Event>.Filter.Eq(e => e.Type, type)
            : Builders<Event>.Filter.Empty;

        var events = await collection
            .Find(filter)
            .Sort(Builders<Event>.Sort.Descending(e => e.CreatedAt))
            .Limit(50)
            .ToListAsync();

        return events;
    }

    public async Task<Event> CreateEventAsync(string type, object payload)
    {
        var database = await _mongoFactory.GetDatabaseAsync();
        var collection = database.GetCollection<Event>("events");

        // Ensure index exists
        await EnsureIndexesAsync(collection);

        var evt = new Event
        {
            Id = ObjectId.GenerateNewId(),
            Type = type,
            Payload = payload,
            CreatedAt = DateTime.UtcNow
        };

        await collection.InsertOneAsync(evt);
        
        _logger.LogInformation("Created event: {EventId} of type: {EventType}", evt.Id, evt.Type);
        
        return evt;
    }

    public async Task<Event?> GetEventByIdAsync(string id)
    {
        if (!ObjectId.TryParse(id, out var objectId))
            return null;

        var database = await _mongoFactory.GetDatabaseAsync();
        var collection = database.GetCollection<Event>("events");

        var evt = await collection
            .Find(e => e.Id == objectId)
            .FirstOrDefaultAsync();

        return evt;
    }

    public async Task<List<Event>> GetEventsByTypeAsync(string type)
    {
        var database = await _mongoFactory.GetDatabaseAsync();
        var collection = database.GetCollection<Event>("events");

        // Ensure index exists
        await EnsureIndexesAsync(collection);

        var events = await collection
            .Find(e => e.Type == type)
            .Sort(Builders<Event>.Sort.Descending(e => e.CreatedAt))
            .ToListAsync();

        return events;
    }

    public async Task<bool> DeleteEventAsync(string id)
    {
        if (!ObjectId.TryParse(id, out var objectId))
            return false;

        var database = await _mongoFactory.GetDatabaseAsync();
        var collection = database.GetCollection<Event>("events");

        var result = await collection.DeleteOneAsync(e => e.Id == objectId);
        return result.DeletedCount > 0;
    }

    private async Task EnsureIndexesAsync(IMongoCollection<Event> collection)
    {
        await _indexSemaphore.WaitAsync();
        try
        {
            // Check if index already exists
            var indexes = await collection.Indexes.ListAsync();
            var indexList = await indexes.ToListAsync();
            
            var hasTypeIndex = indexList.Any(idx => 
                idx["key"].AsBsonDocument.Contains("type") && 
                idx["key"].AsBsonDocument.Contains("created_at"));

            if (!hasTypeIndex)
            {
                var indexKeysDefinition = Builders<Event>.IndexKeys
                    .Ascending(e => e.Type)
                    .Descending(e => e.CreatedAt);

                var indexModel = new CreateIndexModel<Event>(indexKeysDefinition);
                await collection.Indexes.CreateOneAsync(indexModel);
                
                _logger.LogInformation("Created index on events collection for type and created_at");
            }
        }
        finally
        {
            _indexSemaphore.Release();
        }
    }
}
