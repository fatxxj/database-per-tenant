# TenantDbService Integration Tests

This directory contains comprehensive integration tests for the TenantDbService, specifically testing SQL-only and MongoDB-only tenant scenarios.

## Test Files

### `SqlOnlyTenantIntegrationTests.cs`
Comprehensive integration tests for SQL Server-only tenants covering:
- ✅ Tenant creation with SQL Server only
- ✅ Schema creation with multiple tables
- ✅ Index creation (unique and non-unique)
- ✅ Foreign key creation and referential integrity
- ✅ CRUD operations (Create, Read, Update, Delete)
- ✅ Query operations with filters and ordering
- ✅ Multiple schema support
- ✅ Foreign key constraint enforcement

### `MongoDbOnlyTenantIntegrationTests.cs`
Comprehensive integration tests for MongoDB-only tenants covering:
- ✅ Tenant creation with MongoDB only
- ✅ Collection creation with schema
- ✅ Index creation (unique and non-unique)
- ✅ CRUD operations (Create, Read, Update, Delete)
- ✅ Query operations with filters and sorting
- ✅ Nested document support
- ✅ Complex query filters
- ✅ Multiple schema support

### `TestDatabaseFixture.cs`
Test infrastructure that sets up dependency injection and database connections for integration tests.

### `TestHelpers.cs`
Helper methods for setting up test scenarios, including tenant context setup.

## Prerequisites

### Database Setup

**SQL Server:**
- SQL Server 2022 (or compatible version) must be running
- Default connection: `Server=localhost,1433;Database=test_catalog;User Id=sa;Password=P@ssw0rd!;TrustServerCertificate=True;`
- Can be configured via `appsettings.json` or environment variables

**MongoDB:**
- MongoDB 7 (or compatible version) must be running
- Default connection: `mongodb://localhost:27017`
- Can be configured via `appsettings.json` or environment variables

### Configuration

Create `appsettings.json` or `appsettings.Development.json` in the test project root:

```json
{
  "ConnectionStrings": {
    "Catalog": "Server=localhost,1433;Database=test_catalog;User Id=sa;Password=P@ssw0rd!;TrustServerCertificate=True;"
  },
  "SqlServer": {
    "Template": "Server=localhost,1433;Database=tenant_{TENANTID};User Id=sa;Password=P@ssw0rd!;TrustServerCertificate=True;"
  },
  "Mongo": {
    "Template": "mongodb://localhost:27017",
    "DatabaseTemplate": "tenant_{TENANTID}"
  }
}
```

Or set environment variables:
- `ConnectionStrings__Catalog`
- `SqlServer__Template`
- `Mongo__Template`
- `Mongo__DatabaseTemplate`

## Running Tests

### Run All Tests
```bash
dotnet test tests/TenantDbService.Tests/
```

### Run SQL-Only Tests
```bash
dotnet test tests/TenantDbService.Tests/ --filter "FullyQualifiedName~SqlOnlyTenantIntegrationTests"
```

### Run MongoDB-Only Tests
```bash
dotnet test tests/TenantDbService.Tests/ --filter "FullyQualifiedName~MongoDbOnlyTenantIntegrationTests"
```

### Run Specific Test
```bash
dotnet test tests/TenantDbService.Tests/ --filter "FullyQualifiedName~CreateSqlOnlyTenant_ShouldSucceed"
```

## Test Scenarios

### SQL Server Tests

1. **Tenant Creation**: Creates SQL-only tenant and verifies database provisioning
2. **Schema Creation**: Creates e-commerce schema with Customers, Products, Orders, OrderItems tables
3. **Index Creation**: Verifies unique indexes (email, SKU) and non-unique indexes
4. **Foreign Keys**: Tests foreign key relationships between Orders→Customers and OrderItems→Orders/Products
5. **CRUD Operations**: Full Create, Read, Update, Delete operations
6. **Query Operations**: Tests WHERE clauses, ORDER BY, and LIMIT
7. **Referential Integrity**: Verifies foreign key constraints prevent invalid data
8. **Multiple Schemas**: Tests applying multiple schemas to the same tenant

### MongoDB Tests

1. **Tenant Creation**: Creates MongoDB-only tenant and verifies database provisioning
2. **Collection Creation**: Creates content management schema with articles, comments, tags collections
3. **Index Creation**: Verifies unique indexes (slug) and non-unique indexes (authorId, publishedAt)
4. **CRUD Operations**: Full Create, Read, Update, Delete operations
5. **Query Operations**: Tests MongoDB filters, sorting, and limits
6. **Nested Documents**: Tests complex nested document structures
7. **Complex Filters**: Tests MongoDB query operators ($gte, nested fields)
8. **Multiple Schemas**: Tests applying multiple schemas to the same tenant

## Test Data

Tests use realistic schemas:

### SQL E-commerce Schema
- **Customers**: Customer information with email and phone indexes
- **Products**: Product catalog with SKU unique index
- **Orders**: Customer orders with foreign key to Customers
- **OrderItems**: Order line items with foreign keys to Orders and Products

### MongoDB Content Management Schema
- **articles**: Blog articles with slug, authorId, publishedAt indexes
- **comments**: Article comments with articleId and createdAt indexes
- **tags**: Content tags with unique name index

## Cleanup

Tests automatically clean up created tenants in the `Dispose` method. However, if tests fail or are interrupted, you may need to manually clean up:

```sql
-- SQL Server: Drop tenant databases
DROP DATABASE IF EXISTS tenant_<tenant-id>;
```

```javascript
// MongoDB: Drop tenant databases
use tenant_<tenant-id>
db.dropDatabase()
```

## Troubleshooting

### Tests Fail with Connection Errors
- Ensure SQL Server and MongoDB are running
- Check connection strings in configuration
- Verify database credentials

### Tests Fail with Tenant Context Errors
- Ensure `TestHelpers.SetupTenantContextAsync` is called after tenant creation
- Check that tenant was created successfully before setting up context

### Tests Leave Test Data
- Tests should clean up automatically, but if interrupted, manually delete test tenants
- Check catalog database for test tenants: `SELECT * FROM Tenants WHERE Name LIKE 'test-%'`

## Notes

- Tests use unique tenant names with GUIDs to avoid conflicts
- Each test creates its own tenant for isolation
- Tests are designed to run independently (no shared state)
- Database connections are configured via dependency injection
- HttpContext is set up manually for testing (normally done by middleware)


