using System.Text.RegularExpressions;

namespace TenantDbService.Api.Common;

public static class QuerySanitizer
{
    private static readonly Regex SafeWhereClauseRegex = new(
        @"^[\w\s\.,=<>!()'\-+*/%\[\]@]+$", 
        RegexOptions.Compiled);
    
    private static readonly Regex SafeOrderByRegex = new(
        @"^[\w\s,\[\]]+(?:\s+(?:ASC|DESC))?$", 
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly string[] DangerousKeywords = 
    {
        "exec", "execute", "sp_", "xp_", "drop", "create", "alter", 
        "delete", "insert", "update", "union", "declare", "cursor",
        "fetch", "open", "close", "deallocate", "--|", "/*", "*/"
    };

    public static string? SanitizeWhereClause(string? whereClause)
    {
        if (string.IsNullOrWhiteSpace(whereClause))
            return null;

        whereClause = whereClause.Trim();

        if (!SafeWhereClauseRegex.IsMatch(whereClause))
        {
            throw new ArgumentException("WHERE clause contains invalid characters");
        }

        var lowerWhere = whereClause.ToLowerInvariant();
        foreach (var keyword in DangerousKeywords)
        {
            if (lowerWhere.Contains(keyword))
            {
                throw new ArgumentException($"WHERE clause contains dangerous keyword: {keyword}");
            }
        }

        return whereClause;
    }

    public static string? SanitizeOrderBy(string? orderBy)
    {
        if (string.IsNullOrWhiteSpace(orderBy))
            return null;

        orderBy = orderBy.Trim();

        if (!SafeOrderByRegex.IsMatch(orderBy))
        {
            throw new ArgumentException("ORDER BY clause contains invalid characters");
        }

        var lowerOrderBy = orderBy.ToLowerInvariant();
        foreach (var keyword in DangerousKeywords)
        {
            if (lowerOrderBy.Contains(keyword))
            {
                throw new ArgumentException($"ORDER BY clause contains dangerous keyword: {keyword}");
            }
        }

        return orderBy;
    }

    public static int ValidateLimit(int? limit, int maxLimit = 1000)
    {
        if (!limit.HasValue)
            return Constants.Validation.DefaultPageSize;

        if (limit.Value <= 0)
            throw new ArgumentException("Limit must be greater than 0");

        if (limit.Value > maxLimit)
            throw new ArgumentException($"Limit cannot exceed {maxLimit}");

        return limit.Value;
    }
}

