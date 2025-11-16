namespace TenantDbService.Api.Common;

public static class Constants
{
    public static class HttpItems
    {
        public const string TenantContext = "tenant.ctx";
        public const string CorrelationId = "correlation.id";
    }

    public static class Headers
    {
        public const string TenantId = "X-Tenant-Id";
        public const string CorrelationId = "X-Correlation-ID";
        public const string Authorization = "Authorization";
    }

    public static class ClaimTypes
    {
        public const string TenantId = "tenantId";
        public const string TenantIdAlt = "tenant_id";
    }

    public static class TenantStatus
    {
        public const string Active = "active";
        public const string Disabled = "disabled";
    }

    public static class Cache
    {
        public const string TenantConnectionsKeyFormat = "tenant_connections_{0}";
        public const string TenantSchemaKeyFormat = "tenant_schema_{0}";
        public const int DefaultTtlMinutes = 5;
    }

    public static class Validation
    {
        public const int TenantIdMinLength = 6;
        public const int TenantIdMaxLength = 32;
        public const int MaxPageSize = 1000;
        public const int DefaultPageSize = 50;
    }

    public static class ErrorMessages
    {
        public const string TenantIdRequired = "TenantId is required. Provide via JWT claim 'tenantId' or header 'X-Tenant-Id'";
        public const string TenantNotFound = "Tenant '{0}' not found";
        public const string TenantContextNotFound = "Tenant context not found";
        public const string InvalidTenantIdFormat = "Invalid tenantId format. Must be lowercase [a-z0-9-]{6,32}";
        public const string TenantNameRequired = "Tenant name is required";
        public const string TenantAlreadyExists = "Tenant with name '{0}' already exists";
        public const string TableNotFound = "Table '{0}' does not exist";
        public const string InvalidTableName = "Invalid table name format: '{0}'. Table names must be valid SQL identifiers.";
        public const string InvalidColumnName = "Invalid column name: {0}";
        public const string InvalidSchemaDefinition = "Invalid schema definition";
    }

    public static class SchemaDefaults
    {
        public const string DefaultVersion = "1.0";
        public const string OrdersTableName = "Orders";
        public const string EventsCollectionName = "events";
    }
}

