using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Moq;
using TenantDbService.Api.Catalog;
using TenantDbService.Api.Catalog.Entities;
using TenantDbService.Api.Common;
using Xunit;

namespace TenantDbService.Tests;

public class CatalogRepositoryTests
{
    private readonly CatalogDbContext _context;
    private readonly IMemoryCache _cache;
    private readonly Mock<ILogger<CatalogRepository>> _loggerMock;
    private readonly CatalogRepository _repository;

    public CatalogRepositoryTests()
    {
        var options = new DbContextOptionsBuilder<CatalogDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        _context = new CatalogDbContext(options);
        _cache = new MemoryCache(new MemoryCacheOptions());
        _loggerMock = new Mock<ILogger<CatalogRepository>>();
        _repository = new CatalogRepository(_context, _cache, _loggerMock.Object);
    }

    [Fact]
    public async Task GetConnectionsAsync_WhenTenantExists_ReturnsConnections()
    {
        // Arrange
        var tenant = new Tenant
        {
            Id = "test-tenant",
            Name = "Test Tenant",
            Status = "active",
            CreatedAt = DateTime.UtcNow
        };

        var connections = new TenantConnections
        {
            TenantId = "test-tenant",
            SqlServerConnectionString = "Server=test;Database=tenant_test-tenant;",
            MongoDbConnectionString = "mongodb://test:27017",
            MongoDbDatabaseName = "tenant_test-tenant",
            CreatedAt = DateTime.UtcNow
        };

        _context.Tenants.Add(tenant);
        _context.TenantConnections.Add(connections);
        await _context.SaveChangesAsync();

        // Act
        var result = await _repository.GetConnectionsAsync("test-tenant");

        // Assert
        Assert.NotNull(result);
        Assert.Equal("test-tenant", result.TenantId);
        Assert.Equal("Server=test;Database=tenant_test-tenant;", result.SqlServerConnectionString);
    }

    [Fact]
    public async Task GetConnectionsAsync_WhenTenantDisabled_ReturnsNull()
    {
        // Arrange
        var tenant = new Tenant
        {
            Id = "disabled-tenant",
            Name = "Disabled Tenant",
            Status = "disabled",
            CreatedAt = DateTime.UtcNow
        };

        var connections = new TenantConnections
        {
            TenantId = "disabled-tenant",
            SqlServerConnectionString = "Server=test;Database=tenant_disabled-tenant;",
            MongoDbConnectionString = "mongodb://test:27017",
            MongoDbDatabaseName = "tenant_disabled-tenant",
            CreatedAt = DateTime.UtcNow
        };

        _context.Tenants.Add(tenant);
        _context.TenantConnections.Add(connections);
        await _context.SaveChangesAsync();

        // Act
        var result = await _repository.GetConnectionsAsync("disabled-tenant");

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task GetConnectionsAsync_WhenTenantNotExists_ReturnsNull()
    {
        // Act
        var result = await _repository.GetConnectionsAsync("non-existent-tenant");

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task GetConnectionsAsync_WithCaching_ReturnsCachedValue()
    {
        // Arrange
        var tenant = new Tenant
        {
            Id = "cached-tenant",
            Name = "Cached Tenant",
            Status = "active",
            CreatedAt = DateTime.UtcNow
        };

        var connections = new TenantConnections
        {
            TenantId = "cached-tenant",
            SqlServerConnectionString = "Server=test;Database=tenant_cached-tenant;",
            MongoDbConnectionString = "mongodb://test:27017",
            MongoDbDatabaseName = "tenant_cached-tenant",
            CreatedAt = DateTime.UtcNow
        };

        _context.Tenants.Add(tenant);
        _context.TenantConnections.Add(connections);
        await _context.SaveChangesAsync();

        // Act - First call should cache
        var result1 = await _repository.GetConnectionsAsync("cached-tenant");
        
        // Remove from database to verify cache is used
        _context.TenantConnections.Remove(connections);
        await _context.SaveChangesAsync();
        
        // Second call should return cached value
        var result2 = await _repository.GetConnectionsAsync("cached-tenant");

        // Assert
        Assert.NotNull(result1);
        Assert.NotNull(result2);
        Assert.Equal(result1.TenantId, result2.TenantId);
        Assert.Equal(result1.SqlServerConnectionString, result2.SqlServerConnectionString);
    }

    [Fact]
    public async Task TenantExistsAsync_WhenTenantActive_ReturnsTrue()
    {
        // Arrange
        var tenant = new Tenant
        {
            Id = "active-tenant",
            Name = "Active Tenant",
            Status = "active",
            CreatedAt = DateTime.UtcNow
        };

        _context.Tenants.Add(tenant);
        await _context.SaveChangesAsync();

        // Act
        var result = await _repository.TenantExistsAsync("active-tenant");

        // Assert
        Assert.True(result);
    }

    [Fact]
    public async Task TenantExistsAsync_WhenTenantDisabled_ReturnsFalse()
    {
        // Arrange
        var tenant = new Tenant
        {
            Id = "disabled-tenant",
            Name = "Disabled Tenant",
            Status = "disabled",
            CreatedAt = DateTime.UtcNow
        };

        _context.Tenants.Add(tenant);
        await _context.SaveChangesAsync();

        // Act
        var result = await _repository.TenantExistsAsync("disabled-tenant");

        // Assert
        Assert.False(result);
    }

    [Fact]
    public async Task CreateTenantAsync_WithValidData_CreatesTenantAndConnections()
    {
        // Arrange
        var tenant = new Tenant
        {
            Id = "new-tenant",
            Name = "New Tenant",
            Status = "active",
            CreatedAt = DateTime.UtcNow
        };

        var connections = new TenantConnections
        {
            TenantId = "new-tenant",
            SqlServerConnectionString = "Server=test;Database=tenant_new-tenant;",
            MongoDbConnectionString = "mongodb://test:27017",
            MongoDbDatabaseName = "tenant_new-tenant",
            CreatedAt = DateTime.UtcNow
        };

        // Act
        await _repository.CreateTenantAsync(tenant, connections);

        // Assert
        var savedTenant = await _context.Tenants.FindAsync("new-tenant");
        var savedConnections = await _context.TenantConnections.FindAsync("new-tenant");

        Assert.NotNull(savedTenant);
        Assert.NotNull(savedConnections);
        Assert.Equal("New Tenant", savedTenant.Name);
        Assert.Equal("Server=test;Database=tenant_new-tenant;", savedConnections.SqlServerConnectionString);
    }

    [Fact]
    public async Task GetAllTenantsAsync_ReturnsOnlyActiveTenants()
    {
        // Arrange
        var activeTenant = new Tenant
        {
            Id = "active-tenant",
            Name = "Active Tenant",
            Status = "active",
            CreatedAt = DateTime.UtcNow
        };

        var disabledTenant = new Tenant
        {
            Id = "disabled-tenant",
            Name = "Disabled Tenant",
            Status = "disabled",
            CreatedAt = DateTime.UtcNow
        };

        _context.Tenants.AddRange(activeTenant, disabledTenant);
        await _context.SaveChangesAsync();

        // Act
        var result = await _repository.GetAllTenantsAsync();

        // Assert
        Assert.Single(result);
        Assert.Equal("active-tenant", result[0].Id);
        Assert.Equal("Active Tenant", result[0].Name);
    }

    [Fact]
    public async Task DisableTenantAsync_WhenTenantExists_DisablesTenant()
    {
        // Arrange
        var tenant = new Tenant
        {
            Id = "to-disable",
            Name = "To Disable",
            Status = "active",
            CreatedAt = DateTime.UtcNow
        };

        _context.Tenants.Add(tenant);
        await _context.SaveChangesAsync();

        // Act
        var result = await _repository.DisableTenantAsync("to-disable");

        // Assert
        Assert.True(result);
        
        var disabledTenant = await _context.Tenants.FindAsync("to-disable");
        Assert.Equal("disabled", disabledTenant!.Status);
    }

    [Fact]
    public async Task DisableTenantAsync_WhenTenantNotExists_ReturnsFalse()
    {
        // Act
        var result = await _repository.DisableTenantAsync("non-existent");

        // Assert
        Assert.False(result);
    }
}
