using System.Text.RegularExpressions;

namespace TenantDbService.Api.Common;

public static class ValidationHelper
{
    private static readonly Regex TenantIdRegex = new(@"^[a-z0-9\-]{6,32}$", RegexOptions.Compiled);
    private static readonly Regex SqlIdentifierRegex = new(@"^[a-zA-Z_][a-zA-Z0-9_@#$]*$", RegexOptions.Compiled);

    public static bool IsValidTenantId(string? tenantId)
    {
        if (string.IsNullOrWhiteSpace(tenantId))
            return false;

        return tenantId.Length >= Constants.Validation.TenantIdMinLength &&
               tenantId.Length <= Constants.Validation.TenantIdMaxLength &&
               TenantIdRegex.IsMatch(tenantId);
    }

    public static bool IsValidSqlIdentifier(string? identifier)
    {
        if (string.IsNullOrWhiteSpace(identifier))
            return false;

        return SqlIdentifierRegex.IsMatch(identifier);
    }

    public static bool IsValidEmail(string? email)
    {
        if (string.IsNullOrWhiteSpace(email))
            return false;

        try
        {
            var addr = new System.Net.Mail.MailAddress(email);
            return addr.Address == email;
        }
        catch
        {
            return false;
        }
    }

    public static string SanitizeSqlIdentifier(string identifier)
    {
        if (!IsValidSqlIdentifier(identifier))
            throw new ArgumentException($"Invalid SQL identifier: {identifier}");

        return $"[{identifier.Trim('[', ']')}]";
    }

    public static string SanitizeInput(string input, int maxLength = 1000)
    {
        if (string.IsNullOrEmpty(input))
            return input;

        var sanitized = input.Trim();
        
        if (sanitized.Length > maxLength)
            sanitized = sanitized.Substring(0, maxLength);

        return sanitized;
    }
}

