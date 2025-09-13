using Microsoft.Extensions.Logging;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using TenantDbService.Api.Catalog;
using TenantDbService.Api.Common;

namespace TenantDbService.Api.Middleware;

public class TenantResolutionMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<TenantResolutionMiddleware> _logger;

    public TenantResolutionMiddleware(RequestDelegate next, ILogger<TenantResolutionMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context, ICatalogRepository catalogRepository)
    {
        _logger.LogInformation("TenantResolutionMiddleware processing request: {Method} {Path}", context.Request.Method, context.Request.Path);
        
        var tenantId = ResolveTenantId(context);
        
        if (string.IsNullOrEmpty(tenantId))
        {
            // Skip tenant resolution for health checks and auth endpoints
            if (IsPublicEndpoint(context.Request.Path))
            {
                _logger.LogInformation("Skipping tenant resolution for public endpoint: {Path}", context.Request.Path);
                await _next(context);
                return;
            }
            
            _logger.LogWarning("TenantId required but not provided for path: {Path}", context.Request.Path);
            context.Response.StatusCode = 400;
            await context.Response.WriteAsJsonAsync(new { error = "TenantId is required. Provide via JWT claim 'tenantId' or header 'X-Tenant-Id'" });
            return;
        }

        // Validate tenantId format
        if (!IsValidTenantId(tenantId))
        {
            context.Response.StatusCode = 400;
            await context.Response.WriteAsJsonAsync(new { error = "Invalid tenantId format. Must be lowercase [a-z0-9-]{6,32}" });
            return;
        }

        // Check if tenant exists
        var tenantExists = await catalogRepository.TenantExistsAsync(tenantId);
        if (!tenantExists)
        {
            context.Response.StatusCode = 404;
            await context.Response.WriteAsJsonAsync(new { error = $"Tenant '{tenantId}' not found" });
            return;
        }

        // Get tenant connections
        var connections = await catalogRepository.GetConnectionsAsync(tenantId);
        if (connections == null)
        {
            context.Response.StatusCode = 500;
            await context.Response.WriteAsJsonAsync(new { error = "Tenant connections not found" });
            return;
        }

        // Create tenant context
        var tenantContext = new TenantContext
        {
            TenantId = tenantId,
            Connections = connections
        };

        context.Items["tenant.ctx"] = tenantContext;
        
        // Add correlation ID and tenant ID to logging context
        var correlationId = context.Request.Headers["X-Correlation-ID"].FirstOrDefault() ?? Guid.NewGuid().ToString();
        context.Items["correlation.id"] = correlationId;
        
        using var scope = _logger.BeginScope(new Dictionary<string, object>
        {
            ["TenantId"] = tenantId,
            ["CorrelationId"] = correlationId
        });

        _logger.LogDebug("Tenant resolved: {TenantId}", tenantId);

        await _next(context);
    }

    private static string? ResolveTenantId(HttpContext context)
    {
        // First try to get from JWT claim
        var tenantId = GetTenantIdFromJwt(context);
        if (!string.IsNullOrEmpty(tenantId))
            return tenantId;

        // Fallback to header
        return context.Request.Headers["X-Tenant-Id"].FirstOrDefault();
    }

    private static string? GetTenantIdFromJwt(HttpContext context)
    {
        var authHeader = context.Request.Headers["Authorization"].FirstOrDefault();
        if (string.IsNullOrEmpty(authHeader) || !authHeader.StartsWith("Bearer "))
            return null;

        var token = authHeader.Substring("Bearer ".Length);
        
        try
        {
            var handler = new JwtSecurityTokenHandler();
            var jsonToken = handler.ReadJwtToken(token);
            
            var tenantIdClaim = jsonToken.Claims.FirstOrDefault(c => c.Type == "tenantId");
            return tenantIdClaim?.Value;
        }
        catch
        {
            // Token parsing failed, ignore
            return null;
        }
    }

    private static bool IsValidTenantId(string tenantId)
    {
        return !string.IsNullOrEmpty(tenantId) && 
               tenantId.Length >= 6 && 
               tenantId.Length <= 32 && 
               tenantId.All(c => char.IsLower(c) || char.IsDigit(c) || c == '-');
    }

    private static bool IsPublicEndpoint(PathString path)
    {
        var publicPaths = new[]
        {
            "/health/live",
            "/health/ready",
            "/auth/dev-token",
            "/tenants", //for k6 load test
            "/swagger",
            "/swagger/v1/swagger.json"
        };

        return publicPaths.Any(p => path.StartsWithSegments(p));
    }
}
