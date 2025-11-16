using System.Data;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;
using TenantDbService.Api.Catalog.Entities;
using TenantDbService.Api.Common;

namespace TenantDbService.Api.Data.Sql;

public class SqlConnectionFactory
{
    private readonly SqlServerSettings _settings;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public SqlConnectionFactory(IOptions<SqlServerSettings> settings, IHttpContextAccessor httpContextAccessor)
    {
        _settings = settings.Value;
        _httpContextAccessor = httpContextAccessor;
    }

    public async Task<SqlConnection> CreateConnectionAsync()
    {
        var context = _httpContextAccessor.HttpContext;
        if (context?.Items.TryGetValue(Constants.HttpItems.TenantContext, out var tenantCtxObj) != true || 
            tenantCtxObj is not TenantContext tenantCtx)
        {
            throw new InvalidOperationException(Constants.ErrorMessages.TenantContextNotFound);
        }

        if (string.IsNullOrEmpty(tenantCtx.Connections.SqlServerConnectionString))
        {
            throw new InvalidOperationException($"Tenant '{tenantCtx.Tenant.Name}' (ID: {tenantCtx.Tenant.Id}) does not have SQL Server database provisioned. This tenant uses {tenantCtx.Tenant.DatabaseType} database type.");
        }

        var connectionString = tenantCtx.Connections.SqlServerConnectionString;
        var connection = new SqlConnection(connectionString);
        
        return connection;
    }

    public string BuildConnectionString(string tenantId)
    {
        return _settings.Template.Replace("{TENANTID}", tenantId);
    }
}
