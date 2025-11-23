using Microsoft.AspNetCore.Mvc;
using TenantDbService.Api.Auth;
using TenantDbService.Api.Common;

namespace TenantDbService.Api.Endpoints;

public static class AuthEndpoints
{
    public static void MapAuthEndpoints(this WebApplication app)
    {
        app.MapPost("/auth/dev-token", (JwtExtensions jwtExtensions, [FromBody] DevTokenRequest request) =>
        {
            if (string.IsNullOrEmpty(request.TenantId))
                return Results.BadRequest(new { error = Constants.ErrorMessages.TenantIdRequired });
            
            var token = jwtExtensions.GenerateDevToken(request.TenantId);
            return Results.Ok(new { token });
        })
        .WithName("GenerateDevToken")
        .WithTags("Authentication");
    }
}

public record DevTokenRequest(string TenantId);


