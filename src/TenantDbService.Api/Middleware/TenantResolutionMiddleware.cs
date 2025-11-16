using System.IdentityModel.Tokens.Jwt;
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
        _logger.LogDebug("Processing tenant resolution for: {Method} {Path}", context.Request.Method, context.Request.Path);
        
        var tenantId = ResolveTenantId(context);
        
        if (string.IsNullOrEmpty(tenantId))
        {
            if (IsPublicEndpoint(context.Request.Path))
            {
                _logger.LogDebug("Skipping tenant resolution for public endpoint: {Path}", context.Request.Path);
                await _next(context);
                return;
            }
            
            _logger.LogWarning("TenantId required but not provided for path: {Path}", context.Request.Path);
            context.Response.StatusCode = 400;
            await context.Response.WriteAsJsonAsync(new { error = Constants.ErrorMessages.TenantIdRequired });
            return;
        }

        if (!IsValidTenantId(tenantId))
        {
            context.Response.StatusCode = 400;
            await context.Response.WriteAsJsonAsync(new { error = Constants.ErrorMessages.InvalidTenantIdFormat });
            return;
        }

        var tenantExists = await catalogRepository.TenantExistsAsync(tenantId);
        if (!tenantExists)
        {
            context.Response.StatusCode = 404;
            await context.Response.WriteAsJsonAsync(new { error = string.Format(Constants.ErrorMessages.TenantNotFound, tenantId) });
            return;
        }

        var connections = await catalogRepository.GetConnectionsAsync(tenantId);
        if (connections == null)
        {
            context.Response.StatusCode = 500;
            await context.Response.WriteAsJsonAsync(new { error = "Tenant connections not found" });
            return;
        }

        var tenantContext = new TenantContext
        {
            TenantId = tenantId,
            Connections = connections
        };

        context.Items[Constants.HttpItems.TenantContext] = tenantContext;
        
        var correlationId = context.Request.Headers[Constants.Headers.CorrelationId].FirstOrDefault() 
            ?? Guid.NewGuid().ToString();
        context.Items[Constants.HttpItems.CorrelationId] = correlationId;
        
        using var scope = _logger.BeginScope(new Dictionary<string, object>
        {
            ["TenantId"] = tenantId,
            ["CorrelationId"] = correlationId
        });

        _logger.LogDebug("Tenant resolved: {TenantId}", tenantId);

        await _next(context);
    }

    private string? ResolveTenantId(HttpContext context)
    {
        var tenantId = GetTenantIdFromJwt(context);
        if (!string.IsNullOrEmpty(tenantId))
            return tenantId;

        var headerTenantId = context.Request.Headers[Constants.Headers.TenantId].FirstOrDefault();
        if (!string.IsNullOrEmpty(headerTenantId))
        {
            _logger.LogDebug("Found tenantId in header: {TenantId}", headerTenantId);
            return headerTenantId;
        }

        return null;
    }

    private string? GetTenantIdFromJwt(HttpContext context)
    {
        if (context.User?.Identity?.IsAuthenticated == true)
        {
            var tenantIdClaim = context.User.Claims.FirstOrDefault(c => 
                string.Equals(c.Type, Constants.ClaimTypes.TenantId, StringComparison.OrdinalIgnoreCase));
            
            if (tenantIdClaim != null)
            {
                _logger.LogDebug("Found tenantId in authenticated user claims: {TenantId}", tenantIdClaim.Value);
                return tenantIdClaim.Value;
            }
            
            _logger.LogDebug("Authenticated user claims available: {Claims}", 
                string.Join(", ", context.User.Claims.Select(c => $"{c.Type}={c.Value}")));
        }

        var authHeader = context.Request.Headers[Constants.Headers.Authorization].FirstOrDefault();
        if (string.IsNullOrEmpty(authHeader) || !authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var token = authHeader.Substring("Bearer ".Length).Trim();
        
        if (string.IsNullOrEmpty(token))
        {
            return null;
        }
        
        try
        {
            var handler = new JwtSecurityTokenHandler();
            
            if (!handler.CanReadToken(token))
            {
                _logger.LogWarning("JWT token cannot be read");
                return null;
            }
            
            var jsonToken = handler.ReadJwtToken(token);
            
            var tenantIdClaim = jsonToken.Claims.FirstOrDefault(c => 
                string.Equals(c.Type, Constants.ClaimTypes.TenantId, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(c.Type, Constants.ClaimTypes.TenantIdAlt, StringComparison.OrdinalIgnoreCase));
            
            if (tenantIdClaim != null)
            {
                _logger.LogDebug("Found tenantId in JWT claims: {TenantId}", tenantIdClaim.Value);
                return tenantIdClaim.Value;
            }
            
            _logger.LogWarning("JWT token parsed but no tenantId claim found");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to parse JWT token");
        }
        
        return null;
    }

    private static bool IsValidTenantId(string tenantId)
    {
        return !string.IsNullOrEmpty(tenantId) && 
               tenantId.Length >= Constants.Validation.TenantIdMinLength && 
               tenantId.Length <= Constants.Validation.TenantIdMaxLength && 
               tenantId.All(c => char.IsLower(c) || char.IsDigit(c) || c == '-');
    }

    private static bool IsPublicEndpoint(PathString path)
    {
        var publicPaths = new[]
        {
            "/health/live",
            "/health/ready",
            "/auth/dev-token",
            "/tenants",
            "/swagger",
            "/swagger/v1/swagger.json"
        };

        return publicPaths.Any(p => path.StartsWithSegments(p));
    }
}
