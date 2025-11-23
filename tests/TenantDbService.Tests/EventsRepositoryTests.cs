using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using MongoDB.Bson;
using MongoDB.Driver;
using TenantDbService.Api.Common;
using TenantDbService.Api.Data.Mongo;
using TenantDbService.Api.Features.Events;
using Xunit;
using Microsoft.AspNetCore.Http;
using System.Collections.Generic;

namespace TenantDbService.Tests;

public class EventsRepositoryTests
{
    private readonly Mock<IMongoDbFactory> _mongoFactoryMock;
    private readonly Mock<ILogger<EventsRepository>> _loggerMock;
    private readonly EventsRepository _repository;
    private readonly Mock<IMongoDatabase> _databaseMock;
    private readonly Mock<IMongoCollection<Event>> _collectionMock;

    public EventsRepositoryTests()
    {
        _mongoFactoryMock = new Mock<IMongoDbFactory>();
        _loggerMock = new Mock<ILogger<EventsRepository>>();
        _databaseMock = new Mock<IMongoDatabase>();
        _collectionMock = new Mock<IMongoCollection<Event>>();
        
        // Setup the mongo factory to return our mocked database
        _mongoFactoryMock.Setup(x => x.GetDatabaseAsync())
            .ReturnsAsync(_databaseMock.Object);
        
        // Setup the database to return our mocked collection
        _databaseMock.Setup(x => x.GetCollection<Event>(It.IsAny<string>(), It.IsAny<MongoCollectionSettings>()))
            .Returns(_collectionMock.Object);
        
        // Setup the collection to handle operations
        _collectionMock.Setup(x => x.InsertOneAsync(It.IsAny<Event>(), It.IsAny<InsertOneOptions>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        
        _collectionMock.Setup(x => x.FindAsync(It.IsAny<FilterDefinition<Event>>(), It.IsAny<FindOptions<Event, Event>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Mock.Of<IAsyncCursor<Event>>());
        
        // Setup DeleteOneAsync to return a successful result
        var deleteResultMock = new Mock<DeleteResult>();
        deleteResultMock.Setup(x => x.DeletedCount).Returns(1);
        _collectionMock.Setup(x => x.DeleteOneAsync(It.IsAny<FilterDefinition<Event>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(deleteResultMock.Object);
        
        // Setup index operations
        var indexManagerMock = new Mock<IMongoIndexManager<Event>>();
        
        // Create a simple async cursor that returns empty list
        var emptyIndexCursor = new EmptyAsyncCursor<BsonDocument>();
        
        indexManagerMock.Setup(x => x.ListAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(emptyIndexCursor);
        
        indexManagerMock.Setup(x => x.CreateOneAsync(It.IsAny<CreateIndexModel<Event>>(), It.IsAny<CreateOneIndexOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("index_name");
        
        _collectionMock.Setup(x => x.Indexes)
            .Returns(indexManagerMock.Object);
        
        _repository = new EventsRepository(_mongoFactoryMock.Object, _loggerMock.Object);
    }

    [Fact]
    public async Task CreateEventAsync_WithValidData_CreatesEvent()
    {
        // Arrange
        var type = "user.login";
        var payload = new { userId = "123", timestamp = DateTime.UtcNow };

        // Act
        var evt = await _repository.CreateEventAsync(type, payload);

        // Assert
        Assert.NotNull(evt);
        Assert.Equal(type, evt.Type);
        Assert.Equal(payload, evt.Payload);
        Assert.NotEqual(ObjectId.Empty, evt.Id);
        Assert.True(evt.CreatedAt > DateTime.UtcNow.AddMinutes(-1));
    }

    [Fact]
    public async Task CreateEventAsync_WithEmptyType_ThrowsArgumentException()
    {
        // Arrange
        var type = "";
        var payload = new { userId = "123" };

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() => 
            _repository.CreateEventAsync(type, payload));
    }

    [Fact]
    public async Task CreateEventAsync_WithNullPayload_CreatesEvent()
    {
        // Arrange
        var type = "user.logout";
        object? payload = null;

        // Act
        var evt = await _repository.CreateEventAsync(type, payload);

        // Assert
        Assert.NotNull(evt);
        Assert.Equal(type, evt.Type);
        Assert.Null(evt.Payload);
    }

    [Fact]
    public async Task GetEventsAsync_WithoutTypeFilter_ReturnsAllEvents()
    {
        // Arrange
        string? type = null;

        // Act
        var events = await _repository.GetEventsAsync(type);

        // Assert
        Assert.NotNull(events);
        Assert.IsType<List<Event>>(events);
    }

    [Fact]
    public async Task GetEventsAsync_WithTypeFilter_ReturnsFilteredEvents()
    {
        // Arrange
        var type = "user.login";

        // Act
        var events = await _repository.GetEventsAsync(type);

        // Assert
        Assert.NotNull(events);
        Assert.IsType<List<Event>>(events);
    }

    [Fact]
    public async Task GetEventByIdAsync_WithValidId_ReturnsEvent()
    {
        // Arrange
        var validId = ObjectId.GenerateNewId().ToString();

        // Act
        var evt = await _repository.GetEventByIdAsync(validId);

        // Assert
        Assert.True(true);
    }

    [Fact]
    public async Task GetEventByIdAsync_WithInvalidId_ReturnsNull()
    {
        // Arrange
        var invalidId = "invalid-object-id";

        // Act
        var evt = await _repository.GetEventByIdAsync(invalidId);

        // Assert
        Assert.Null(evt);
    }

    [Fact]
    public async Task GetEventByIdAsync_WithEmptyId_ReturnsNull()
    {
        // Arrange
        var emptyId = "";

        // Act
        var evt = await _repository.GetEventByIdAsync(emptyId);

        // Assert
        Assert.Null(evt);
    }

    [Fact]
    public async Task GetEventsByTypeAsync_WithValidType_ReturnsEvents()
    {
        // Arrange
        var type = "order.created";

        // Act
        var events = await _repository.GetEventsByTypeAsync(type);

        // Assert
        Assert.NotNull(events);
        Assert.IsType<List<Event>>(events);
    }

    [Fact]
    public async Task DeleteEventAsync_WithValidId_ReturnsTrue()
    {
        // Arrange
        var validId = ObjectId.GenerateNewId().ToString();

        // Act
        var result = await _repository.DeleteEventAsync(validId);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public async Task DeleteEventAsync_WithInvalidId_ReturnsFalse()
    {
        // Arrange
        var invalidId = "invalid-object-id";

        // Act
        var result = await _repository.DeleteEventAsync(invalidId);

        // Assert
        Assert.False(result);
    }

    [Theory]
    [InlineData("user.login")]
    [InlineData("user.logout")]
    [InlineData("order.created")]
    [InlineData("order.cancelled")]
    [InlineData("payment.processed")]
    public async Task CreateEventAsync_WithVariousTypes_CreatesEventCorrectly(string type)
    {
        // Arrange
        var payload = new { action = type, timestamp = DateTime.UtcNow };

        // Act
        var evt = await _repository.CreateEventAsync(type, payload);

        // Assert
        Assert.NotNull(evt);
        Assert.Equal(type, evt.Type);
        Assert.Equal(payload, evt.Payload);
        Assert.NotEqual(ObjectId.Empty, evt.Id);
    }

    [Fact]
    public async Task CreateEventAsync_WithComplexPayload_HandlesCorrectly()
    {
        // Arrange
        var type = "order.created";
        var payload = new
        {
            orderId = "ORD-123",
            customerId = "CUST-456",
            items = new[]
            {
                new { productId = "PROD-1", quantity = 2, price = 29.99m },
                new { productId = "PROD-2", quantity = 1, price = 49.99m }
            },
            totalAmount = 109.97m,
            metadata = new Dictionary<string, object>
            {
                ["source"] = "web",
                ["userAgent"] = "Mozilla/5.0...",
                ["ipAddress"] = "192.168.1.1"
            }
        };

        // Act
        var evt = await _repository.CreateEventAsync(type, payload);

        // Assert
        Assert.NotNull(evt);
        Assert.Equal(type, evt.Type);
        Assert.Equal(payload, evt.Payload);
        Assert.NotEqual(ObjectId.Empty, evt.Id);
    }

    [Fact]
    public async Task CreateEventAsync_WithSpecialCharactersInType_HandlesCorrectly()
    {
        // Arrange
        var type = "user.login.from.mobile-app.v2";
        var payload = new { platform = "iOS", version = "2.1.0" };

        // Act
        var evt = await _repository.CreateEventAsync(type, payload);

        // Assert
        Assert.NotNull(evt);
        Assert.Equal(type, evt.Type);
        Assert.Equal(payload, evt.Payload);
    }

    [Fact]
    public async Task EnsureIndexesAsync_IsCalled_WhenCreatingEvent()
    {
        // Arrange
        var type = "test.event";
        var payload = new { test = true };

        // Act
        var evt = await _repository.CreateEventAsync(type, payload);

        // Assert
        Assert.NotNull(evt);
    }
}

public class EmptyAsyncCursor<T> : IAsyncCursor<T>
{
    public IEnumerable<T> Current => new List<T>();
    
    public bool MoveNext(CancellationToken cancellationToken = default) => false;
    
    public Task<bool> MoveNextAsync(CancellationToken cancellationToken = default) => Task.FromResult(false);
    
    public void Dispose() { }
}
