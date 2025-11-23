using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Moq;
using System.Security.Claims;
using System.Text.Json;
using TenantDbService.Api.Catalog;
using TenantDbService.Api.Catalog.Entities;
using TenantDbService.Api.Common;
using TenantDbService.Api.Middleware;
using Xunit;

namespace TenantDbService.Tests;

public class TenantResolutionMiddlewareTests
{
    private readonly Mock<ILogger<TenantResolutionMiddleware>> _loggerMock;
    private readonly Mock<ICatalogRepository> _catalogRepositoryMock;
    private readonly TenantResolutionMiddleware _middleware;

    public TenantResolutionMiddlewareTests()
    {
        _loggerMock = new Mock<ILogger<TenantResolutionMiddleware>>();
        _catalogRepositoryMock = new Mock<ICatalogRepository>();
        _middleware = new TenantResolutionMiddleware(Next, _loggerMock.Object);
    }

    [Fact]
    public async Task InvokeAsync_WithValidJwtClaim_ResolvesTenant()
    {
        // Arrange
        var context = CreateHttpContext();
        var tenantId = "test-tenant";
        
        // Create JWT with tenant claim
        var token = CreateJwtToken(tenantId);
        context.Request.Headers["Authorization"] = $"Bearer {token}";

        var tenant = new Tenant
        {
            Id = tenantId,
            Name = "Test Tenant",
            Status = "active"
        };

        var connections = new TenantConnections
        {
            TenantId = tenantId,
            SqlServerConnectionString = "Server=test;Database=tenant_test-tenant;",
            MongoDbConnectionString = "mongodb://test:27017",
            MongoDbDatabaseName = "tenant_test-tenant"
        };

        _catalogRepositoryMock.Setup(x => x.TenantExistsAsync(tenantId)).ReturnsAsync(true);
        _catalogRepositoryMock.Setup(x => x.GetTenantAsync(tenantId)).ReturnsAsync(tenant);
        _catalogRepositoryMock.Setup(x => x.GetConnectionsAsync(tenantId)).ReturnsAsync(connections);

        // Act
        await _middleware.InvokeAsync(context, _catalogRepositoryMock.Object);

        // Assert
        Assert.Equal(200, context.Response.StatusCode);
        Assert.True(context.Items.ContainsKey("tenant.ctx"));
        
        var tenantContext = context.Items["tenant.ctx"] as TenantContext;
        Assert.NotNull(tenantContext);
        Assert.Equal(tenantId, tenantContext.TenantId);
    }

    [Fact]
    public async Task InvokeAsync_WithValidHeader_ResolvesTenant()
    {
        // Arrange
        var context = CreateHttpContext();
        var tenantId = "test-tenant";
        
        context.Request.Headers["X-Tenant-Id"] = tenantId;

        var tenant = new Tenant
        {
            Id = tenantId,
            Name = "Test Tenant",
            Status = "active"
        };

        var connections = new TenantConnections
        {
            TenantId = tenantId,
            SqlServerConnectionString = "Server=test;Database=tenant_test-tenant;",
            MongoDbConnectionString = "mongodb://test:27017",
            MongoDbDatabaseName = "tenant_test-tenant"
        };

        _catalogRepositoryMock.Setup(x => x.TenantExistsAsync(tenantId)).ReturnsAsync(true);
        _catalogRepositoryMock.Setup(x => x.GetTenantAsync(tenantId)).ReturnsAsync(tenant);
        _catalogRepositoryMock.Setup(x => x.GetConnectionsAsync(tenantId)).ReturnsAsync(connections);

        // Act
        await _middleware.InvokeAsync(context, _catalogRepositoryMock.Object);

        // Assert
        Assert.Equal(200, context.Response.StatusCode);
        Assert.True(context.Items.ContainsKey("tenant.ctx"));
        
        var tenantContext = context.Items["tenant.ctx"] as TenantContext;
        Assert.NotNull(tenantContext);
        Assert.Equal(tenantId, tenantContext.TenantId);
    }

    [Fact]
    public async Task InvokeAsync_WithInvalidTenantId_Returns400()
    {
        // Arrange
        var context = CreateHttpContext();
        var invalidTenantId = "INVALID_TENANT_ID"; // Contains uppercase and underscore
        
        context.Request.Headers["X-Tenant-Id"] = invalidTenantId;

        // Act
        await _middleware.InvokeAsync(context, _catalogRepositoryMock.Object);

        // Assert
        Assert.Equal(400, context.Response.StatusCode);
        
        var responseBody = await GetResponseBody(context);
        var errorResponse = JsonSerializer.Deserialize<Dictionary<string, string>>(responseBody);
        Assert.Contains("Invalid tenantId format", errorResponse!["error"]);
    }

    [Fact]
    public async Task InvokeAsync_WithNonExistentTenant_Returns404()
    {
        // Arrange
        var context = CreateHttpContext();
        var tenantId = "non-existent-tenant";
        
        context.Request.Headers["X-Tenant-Id"] = tenantId;

        _catalogRepositoryMock.Setup(x => x.TenantExistsAsync(tenantId)).ReturnsAsync(false);

        // Act
        await _middleware.InvokeAsync(context, _catalogRepositoryMock.Object);

        // Assert
        Assert.Equal(404, context.Response.StatusCode);
        
        var responseBody = await GetResponseBody(context);
        var errorResponse = JsonSerializer.Deserialize<Dictionary<string, string>>(responseBody);
        Assert.Contains("not found", errorResponse!["error"]);
    }

    [Fact]
    public async Task InvokeAsync_WithMissingTenantId_Returns400()
    {
        // Arrange
        var context = CreateHttpContext();

        // Act
        await _middleware.InvokeAsync(context, _catalogRepositoryMock.Object);

        // Assert
        Assert.Equal(400, context.Response.StatusCode);
        
        var responseBody = await GetResponseBody(context);
        var errorResponse = JsonSerializer.Deserialize<Dictionary<string, string>>(responseBody);
        Assert.Contains("TenantId is required", errorResponse!["error"]);
    }

    [Fact]
    public async Task InvokeAsync_WithPublicEndpoint_SkipsTenantResolution()
    {
        // Arrange
        var context = CreateHttpContext();
        context.Request.Path = "/health/live";

        // Act
        await _middleware.InvokeAsync(context, _catalogRepositoryMock.Object);

        // Assert
        Assert.Equal(200, context.Response.StatusCode);
        Assert.False(context.Items.ContainsKey("tenant.ctx"));
    }

    [Fact]
    public async Task InvokeAsync_WithTenantConnectionsNotFound_Returns500()
    {
        // Arrange
        var context = CreateHttpContext();
        var tenantId = "test-tenant";
        
        context.Request.Headers["X-Tenant-Id"] = tenantId;

        var tenant = new Tenant
        {
            Id = tenantId,
            Name = "Test Tenant",
            Status = "active"
        };

        _catalogRepositoryMock.Setup(x => x.TenantExistsAsync(tenantId)).ReturnsAsync(true);
        _catalogRepositoryMock.Setup(x => x.GetTenantAsync(tenantId)).ReturnsAsync(tenant);
        _catalogRepositoryMock.Setup(x => x.GetConnectionsAsync(tenantId)).ReturnsAsync((TenantConnections?)null);

        // Act
        await _middleware.InvokeAsync(context, _catalogRepositoryMock.Object);

        // Assert
        Assert.Equal(500, context.Response.StatusCode);
        
        var responseBody = await GetResponseBody(context);
        var errorResponse = JsonSerializer.Deserialize<Dictionary<string, string>>(responseBody);
        Assert.Contains("connections not found", errorResponse!["error"]);
    }

    [Fact]
    public async Task InvokeAsync_WithCorrelationId_IncludesInContext()
    {
        // Arrange
        var context = CreateHttpContext();
        var tenantId = "test-tenant";
        var correlationId = "test-correlation-id";
        
        context.Request.Headers["X-Tenant-Id"] = tenantId;
        context.Request.Headers["X-Correlation-ID"] = correlationId;

        var tenant = new Tenant
        {
            Id = tenantId,
            Name = "Test Tenant",
            Status = "active"
        };

        var connections = new TenantConnections
        {
            TenantId = tenantId,
            SqlServerConnectionString = "Server=test;Database=tenant_test-tenant;",
            MongoDbConnectionString = "mongodb://test:27017",
            MongoDbDatabaseName = "tenant_test-tenant"
        };

        _catalogRepositoryMock.Setup(x => x.TenantExistsAsync(tenantId)).ReturnsAsync(true);
        _catalogRepositoryMock.Setup(x => x.GetTenantAsync(tenantId)).ReturnsAsync(tenant);
        _catalogRepositoryMock.Setup(x => x.GetConnectionsAsync(tenantId)).ReturnsAsync(connections);

        // Act
        await _middleware.InvokeAsync(context, _catalogRepositoryMock.Object);

        // Assert
        Assert.True(context.Items.ContainsKey("correlation.id"));
        Assert.Equal(correlationId, context.Items["correlation.id"]);
    }

    private static HttpContext CreateHttpContext()
    {
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();
        context.Request.Path = "/api/orders";
        return context;
    }

    private static string CreateJwtToken(string tenantId)
    {
        var claims = new[]
        {
            new Claim("tenantId", tenantId),
            new Claim("sub", "test-user"),
            new Claim("name", "Test User"),
            new Claim("role", "admin")
        };

        var token = new System.IdentityModel.Tokens.Jwt.JwtSecurityToken(
            issuer: "TenantDbService",
            audience: "TenantDbService",
            claims: claims,
            expires: DateTime.UtcNow.AddHours(1),
            signingCredentials: new Microsoft.IdentityModel.Tokens.SigningCredentials(
                new Microsoft.IdentityModel.Tokens.SymmetricSecurityKey(
                    System.Text.Encoding.UTF8.GetBytes("test-key-with-at-least-32-characters")),
                Microsoft.IdentityModel.Tokens.SecurityAlgorithms.HmacSha256)
        );

        return new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler().WriteToken(token);
    }

    private static async Task<string> GetResponseBody(HttpContext context)
    {
        context.Response.Body.Seek(0, SeekOrigin.Begin);
        using var reader = new StreamReader(context.Response.Body);
        return await reader.ReadToEndAsync();
    }

    private static Task Next(HttpContext context)
    {
        context.Response.StatusCode = 200;
        return Task.CompletedTask;
    }
}
