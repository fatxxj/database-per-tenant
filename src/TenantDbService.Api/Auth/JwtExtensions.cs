using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;

namespace TenantDbService.Api.Auth;

public class JwtSettings
{
    public string Issuer { get; set; } = string.Empty;
    public string Audience { get; set; } = string.Empty;
    public string Key { get; set; } = string.Empty;
}

public class JwtExtensions
{
    private readonly JwtSettings _settings;

    public JwtExtensions(IOptions<JwtSettings> settings)
    {
        _settings = settings.Value;
    }

    public string GenerateDevToken(string tenantId)
    {
        if (string.IsNullOrEmpty(tenantId))
            throw new ArgumentException("TenantId is required", nameof(tenantId));

        if (!IsValidTenantId(tenantId))
            throw new ArgumentException("Invalid tenantId format", nameof(tenantId));

        var tokenHandler = new JwtSecurityTokenHandler();
        var key = Encoding.UTF8.GetBytes(_settings.Key);
        
        var tokenDescriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(new[]
            {
                new Claim("tenantId", tenantId),
                new Claim("sub", "dev-user"),
                new Claim("name", "Development User"),
                new Claim("role", "admin")
            }),
            Expires = DateTime.UtcNow.AddHours(24),
            Issuer = _settings.Issuer,
            Audience = _settings.Audience,
            SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(key), SecurityAlgorithms.HmacSha256Signature)
        };

        var token = tokenHandler.CreateToken(tokenDescriptor);
        return tokenHandler.WriteToken(token);
    }

    private static bool IsValidTenantId(string tenantId)
    {
        return !string.IsNullOrEmpty(tenantId) && 
               tenantId.Length >= 6 && 
               tenantId.Length <= 32 && 
               tenantId.All(c => char.IsLower(c) || char.IsDigit(c) || c == '-');
    }
}
