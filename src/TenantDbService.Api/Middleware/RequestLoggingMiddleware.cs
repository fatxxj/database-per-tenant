using System.Diagnostics;
using TenantDbService.Api.Common;

namespace TenantDbService.Api.Middleware;

public class RequestLoggingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<RequestLoggingMiddleware> _logger;

    public RequestLoggingMiddleware(RequestDelegate next, ILogger<RequestLoggingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var correlationId = context.Request.Headers[Constants.Headers.CorrelationId].FirstOrDefault()
            ?? Guid.NewGuid().ToString();
        
        context.Items[Constants.HttpItems.CorrelationId] = correlationId;
        context.Response.Headers.Add(Constants.Headers.CorrelationId, correlationId);

        var stopwatch = Stopwatch.StartNew();
        var tenantId = context.Items.TryGetValue(Constants.HttpItems.TenantContext, out var tenantCtxObj) && 
                       tenantCtxObj is TenantContext tenantCtx
            ? tenantCtx.TenantId
            : "anonymous";

        _logger.LogInformation(
            "Request started: {Method} {Path} | TenantId: {TenantId} | CorrelationId: {CorrelationId}",
            context.Request.Method,
            context.Request.Path,
            tenantId,
            correlationId);

        try
        {
            await _next(context);
        }
        finally
        {
            stopwatch.Stop();
            
            _logger.LogInformation(
                "Request completed: {Method} {Path} | Status: {StatusCode} | Duration: {Duration}ms | TenantId: {TenantId} | CorrelationId: {CorrelationId}",
                context.Request.Method,
                context.Request.Path,
                context.Response.StatusCode,
                stopwatch.ElapsedMilliseconds,
                tenantId,
                correlationId);
        }
    }
}

