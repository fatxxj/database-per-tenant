# TenantDbService - API Example Payloads

This folder contains comprehensive examples for all API endpoints, organized by database type.

## 📁 Folder Structure

```
example-payloads/
├── mongodb-only/          # Examples for MongoDB-only tenants (databaseType: 2)
├── sql-only/              # Examples for SQL Server-only tenants (databaseType: 1)
├── both/                  # Examples for hybrid tenants (databaseType: 3)
├── common/                # Examples that work for all tenant types
└── README.txt             # This file
```

## 📂 Folder Contents

### 🍃 mongodb-only/ (MongoDB-Only Tenants)
Examples for tenants that use MongoDB database only (databaseType: 2):

- **03-create-tenant-mongodb-only.txt** - Create MongoDB-only tenant
- **12-insert-into-collection.txt** - Insert documents into collections
- **13-update-collection-document.txt** - Update MongoDB documents
- **14-query-collection-with-filters.txt** - Query with MongoDB filters
- **16-create-event.txt** - Create events (MongoDB)
- **17-query-events-by-type.txt** - Query events by type

### 🗄️ sql-only/ (SQL Server-Only Tenants)
Examples for tenants that use SQL Server database only (databaseType: 1):

- **02-create-tenant-sqlserver-only.txt** - Create SQL Server-only tenant
- **08-create-table.txt** - Create custom SQL tables
- **09-insert-into-table.txt** - Insert records into tables
- **10-update-table-record.txt** - Update table records
- **11-query-table-with-filters.txt** - Query with WHERE, ORDER BY, LIMIT
- **15-create-order.txt** - Create orders (default SQL table)

### 🔄 both/ (Hybrid Tenants)
Examples for tenants that use both SQL Server and MongoDB (databaseType: 3):

- **04-create-tenant-both-databases.txt** - Create hybrid tenant
- **05-create-tenant-with-schema.txt** - Create tenant with custom schema (tables + collections)
- **06-update-tenant-schema.txt** - Update tenant schema
- **18-complete-workflow-example.txt** - **Full end-to-end workflow** (START HERE!)

### 🌐 common/ (All Tenant Types)
Examples that work for all tenant types regardless of database configuration:

- **01-auth-dev-token.txt** - Generate JWT token (all tenants)
- **07-validate-schema.txt** - Validate schema definition
- **19-health-checks.txt** - Health and readiness checks
- **20-list-and-get-tenants.txt** - List all tenants & get schema
- **21-get-table-and-collection-lists.txt** - List tables/collections
- **22-get-specific-records.txt** - Get records by ID (SQL & MongoDB)
- **23-delete-records.txt** - Delete records (SQL & MongoDB)

## 🚀 Quick Start Guide

### Step 1: Choose Your Tenant Type
Based on your application needs:

- **SQL Server Only** → Use examples from `sql-only/` folder
- **MongoDB Only** → Use examples from `mongodb-only/` folder
- **Both Databases** → Use examples from `both/` folder

### Step 2: Create Your Tenant
1. **SQL Only**: `sql-only/02-create-tenant-sqlserver-only.txt`
2. **MongoDB Only**: `mongodb-only/03-create-tenant-mongodb-only.txt`
3. **Both**: `both/04-create-tenant-both-databases.txt`
4. **With Schema**: `both/05-create-tenant-with-schema.txt`

### Step 3: Authenticate
Use `common/01-auth-dev-token.txt` to generate your JWT token.

### Step 4: Follow Examples
- **Complete Workflow**: Start with `both/18-complete-workflow-example.txt`
- **SQL Operations**: See `sql-only/` folder
- **MongoDB Operations**: See `mongodb-only/` folder
- **Common Operations**: See `common/` folder

## 📋 Database Type Values

| Value | Enum | Folder | Description |
|-------|------|--------|-------------|
| 1 | `SqlServer` | `sql-only/` | SQL Server only - for relational data |
| 2 | `MongoDb` | `mongodb-only/` | MongoDB only - for document data |
| 3 | `Both` | `both/` | Both databases - for hybrid applications |

## 🔐 Authentication

All protected endpoints require:

```
Authorization: Bearer {your-jwt-token}
X-Tenant-Id: {your-tenant-id}
```

Generate token using `common/01-auth-dev-token.txt`

## ⚠️ Important Notes

### Database Type Compatibility
- **SQL Server endpoints** only work with tenants that have `databaseType: 1` or `3`
- **MongoDB endpoints** only work with tenants that have `databaseType: 2` or `3`
- Attempting to use wrong endpoint returns clear error message

### Security
- Development tokens (`/auth/dev-token`) are for testing only
- Use proper OAuth/OIDC in production
- WHERE clauses are sanitized for SQL injection prevention
- Query parameters have limits (max 1000 records)
- Dangerous SQL keywords are blocked

## 📖 File Naming Convention

Files are numbered for easy reference:
- **01-09**: Authentication & tenant management
- **10-19**: Data operations (SQL & MongoDB)
- **20-23**: Common operations & utilities

## 🎯 Use Cases

### SQL Server Only (`sql-only/`)
Perfect for SaaS applications that:
- Require ACID transactions and relational integrity
- Use structured data with complex relationships
- Need advanced query capabilities (joins, aggregations)
- Examples: ERP systems, CRM platforms, financial applications

### MongoDB Only (`mongodb-only/`)
Ideal for SaaS applications that:
- Work with flexible, document-based data
- Need high-write throughput for events/logs
- Require schema flexibility and rapid iteration
- Examples: Analytics platforms, IoT applications, content management systems

### Both Databases (`both/`)
Best for SaaS applications that:
- Have mixed data requirements (structured + flexible)
- Store transactional data in SQL + events/logs in MongoDB
- Need different databases for different features
- Examples: E-commerce platforms, healthcare systems, complex enterprise applications

## 📚 Additional Resources

- See main `README.md` for full API documentation
- Check Swagger UI at http://localhost:8080/swagger
- Review `both/18-complete-workflow-example.txt` for step-by-step guide

## 🔄 Migration Notes

If you have existing tenants:
- SQL-only tenants → Use `sql-only/` examples
- MongoDB-only tenants → Use `mongodb-only/` examples
- Hybrid tenants → Use `both/` examples
- All tenants → Use `common/` examples

---

**Need Help?** Start with `both/18-complete-workflow-example.txt` for a complete walkthrough!
