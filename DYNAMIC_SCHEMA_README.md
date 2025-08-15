# Dynamic Schema System

Your TenantDbService now supports **truly dynamic schema creation** where any SaaS application can define their own tables, columns, relationships, and indexes. This system allows complete customization of the database structure per tenant.

## Key Features

✅ **Dynamic Table Creation** - Create any tables with custom columns  
✅ **Flexible Data Types** - Support for all SQL Server data types  
✅ **Index Management** - Create custom indexes for performance  
✅ **Foreign Key Relationships** - Define complex table relationships  
✅ **MongoDB Collections** - Dynamic document collections with indexes  
✅ **Schema Validation** - Built-in validation to prevent errors  
✅ **Schema Versioning** - Track schema changes over time  
✅ **Dynamic Data Access** - CRUD operations on any schema  

## How It Works

### 1. Schema Definition
Each tenant can define their schema using JSON:

```json
{
  "version": "1.0",
  "name": "My SaaS Schema",
  "description": "Custom schema for my application",
  "tables": [
    {
      "name": "Users",
      "description": "User accounts",
      "columns": [
        {
          "name": "Id",
          "dataType": "nvarchar",
          "maxLength": 50,
          "isPrimaryKey": true,
          "isNullable": false
        },
        {
          "name": "Email",
          "dataType": "nvarchar",
          "maxLength": 255,
          "isNullable": false
        },
        {
          "name": "CreatedAt",
          "dataType": "datetime2",
          "isNullable": false,
          "defaultValue": "GETUTCDATE()"
        }
      ],
      "indexes": [
        {
          "name": "IX_Users_Email",
          "columns": ["Email"],
          "isUnique": true
        }
      ]
    }
  ],
  "collections": [
    {
      "name": "user_sessions",
      "description": "User session data",
      "indexes": [
        {
          "name": "IX_user_sessions_user_id",
          "columns": ["user_id"],
          "isUnique": false
        }
      ]
    }
  ]
}
```

### 2. Tenant Creation with Schema

```bash
# Create tenant with custom schema
curl -X POST "https://your-api/tenants" \
  -H "Content-Type: application/json" \
  -d '{
    "name": "my-saas-app",
    "schemaDefinition": {
      "version": "1.0",
      "name": "My SaaS Schema",
      "tables": [...],
      "collections": [...]
    }
  }'
```

### 3. Dynamic Data Access

Once the schema is created, you can perform CRUD operations on any table:

```bash
# Insert data into custom table
curl -X POST "https://your-api/api/data/tables/Users" \
  -H "Authorization: Bearer YOUR_JWT_TOKEN" \
  -H "Content-Type: application/json" \
  -d '{
    "Id": "user123",
    "Email": "user@example.com",
    "Name": "John Doe"
  }'

# Query data with filters
curl -X GET "https://your-api/api/data/tables/Users?where=Email='user@example.com'&orderBy=CreatedAt DESC" \
  -H "Authorization: Bearer YOUR_JWT_TOKEN"
```

## Supported Data Types

### SQL Server Types
- `string`, `varchar`, `nvarchar` - String data
- `text`, `ntext` - Large text data
- `int`, `bigint`, `smallint`, `tinyint` - Integer types
- `decimal` - Decimal numbers (requires precision/scale)
- `money` - Currency values
- `float`, `real` - Floating point numbers
- `bit` - Boolean values
- `datetime`, `datetime2`, `date`, `time` - Date/time types
- `uniqueidentifier` - GUID values
- `binary`, `varbinary`, `image` - Binary data

### MongoDB Collections
- Document collections with flexible schema
- Custom indexes for performance
- JSON validation schemas (optional)

## API Endpoints

### Schema Management
- `POST /tenants` - Create tenant with schema
- `POST /tenants/{tenantId}/schema` - Update tenant schema
- `GET /tenants/{tenantId}/schema` - Get tenant schema
- `POST /schema/validate` - Validate schema definition

### Dynamic Data Access (SQL Server)
- `GET /api/data/tables` - List all tables
- `GET /api/data/tables/{tableName}/schema` - Get table schema
- `GET /api/data/tables/{tableName}` - Query table data
- `GET /api/data/tables/{tableName}/{id}` - Get record by ID
- `POST /api/data/tables/{tableName}` - Insert new record
- `PUT /api/data/tables/{tableName}/{id}` - Update record
- `DELETE /api/data/tables/{tableName}/{id}` - Delete record

### Dynamic Data Access (MongoDB)
- `GET /api/data/collections` - List all collections
- `GET /api/data/collections/{collectionName}` - Query collection
- `GET /api/data/collections/{collectionName}/{id}` - Get document by ID
- `POST /api/data/collections/{collectionName}` - Insert new document
- `PUT /api/data/collections/{collectionName}/{id}` - Update document
- `DELETE /api/data/collections/{collectionName}/{id}` - Delete document

## Query Parameters

### SQL Server Queries
- `where` - WHERE clause (e.g., "Email='user@example.com'")
- `orderBy` - ORDER BY clause (e.g., "CreatedAt DESC")
- `limit` - Maximum number of records to return

### MongoDB Queries
- `filter` - JSON filter (e.g., '{"user_id": "123"}')
- `sort` - JSON sort (e.g., '{"created_at": -1}')
- `limit` - Maximum number of documents to return

## Example Use Cases

### 1. E-commerce Application
```json
{
  "tables": [
    {
      "name": "Products",
      "columns": [
        {"name": "Id", "dataType": "nvarchar", "maxLength": 50, "isPrimaryKey": true},
        {"name": "Name", "dataType": "nvarchar", "maxLength": 200},
        {"name": "Price", "dataType": "decimal", "precision": 18, "scale": 2}
      ]
    },
    {
      "name": "Orders",
      "columns": [
        {"name": "Id", "dataType": "nvarchar", "maxLength": 50, "isPrimaryKey": true},
        {"name": "CustomerId", "dataType": "nvarchar", "maxLength": 50},
        {"name": "TotalAmount", "dataType": "decimal", "precision": 18, "scale": 2}
      ],
      "foreignKeys": [
        {
          "name": "FK_Orders_Customers",
          "referencedTable": "Customers",
          "columns": ["CustomerId"],
          "referencedColumns": ["Id"]
        }
      ]
    }
  ]
}
```

### 2. CRM Application
```json
{
  "tables": [
    {
      "name": "Contacts",
      "columns": [
        {"name": "Id", "dataType": "nvarchar", "maxLength": 50, "isPrimaryKey": true},
        {"name": "FirstName", "dataType": "nvarchar", "maxLength": 100},
        {"name": "LastName", "dataType": "nvarchar", "maxLength": 100},
        {"name": "Email", "dataType": "nvarchar", "maxLength": 255}
      ],
      "indexes": [
        {
          "name": "IX_Contacts_Email",
          "columns": ["Email"],
          "isUnique": true
        }
      ]
    }
  ]
}
```

### 3. Project Management Application
```json
{
  "tables": [
    {
      "name": "Projects",
      "columns": [
        {"name": "Id", "dataType": "nvarchar", "maxLength": 50, "isPrimaryKey": true},
        {"name": "Name", "dataType": "nvarchar", "maxLength": 200},
        {"name": "Status", "dataType": "nvarchar", "maxLength": 20},
        {"name": "StartDate", "dataType": "date"},
        {"name": "EndDate", "dataType": "date"}
      ]
    }
  ],
  "collections": [
    {
      "name": "project_activities",
      "indexes": [
        {
          "name": "IX_project_activities_project_id",
          "columns": ["project_id"]
        }
      ]
    }
  ]
}
```

## Schema Validation

The system validates schemas before creation:

- ✅ Table names are unique
- ✅ Column names are unique within tables
- ✅ Primary keys are defined
- ✅ Foreign key references are valid
- ✅ Data type constraints are met
- ✅ Index names are unique

## Best Practices

1. **Use Descriptive Names** - Clear table and column names
2. **Define Primary Keys** - Every table should have a primary key
3. **Add Indexes** - Index frequently queried columns
4. **Use Foreign Keys** - Maintain referential integrity
5. **Version Your Schemas** - Track schema changes
6. **Validate Before Deploy** - Use the validation endpoint
7. **Plan for Growth** - Consider future schema evolution

## Migration Strategy

For production use, consider implementing:

1. **Schema Versioning** - Track schema changes over time
2. **Migration Scripts** - Handle schema evolution
3. **Backup Strategy** - Backup before schema changes
4. **Rollback Plan** - Ability to revert schema changes
5. **Testing Environment** - Test schema changes first

## Security Considerations

- All endpoints require JWT authentication
- Tenant isolation is enforced at the database level
- SQL injection protection through parameterized queries
- Schema validation prevents malicious schema definitions

## Performance Tips

1. **Index Strategy** - Index columns used in WHERE, ORDER BY, and JOIN clauses
2. **Data Types** - Choose appropriate data types and sizes
3. **Partitioning** - Consider table partitioning for large datasets
4. **Query Optimization** - Use efficient WHERE clauses
5. **Connection Pooling** - Leverage connection pooling for better performance

This dynamic schema system gives you complete flexibility to create any database structure your SaaS application needs, while maintaining the security and isolation benefits of the multi-tenant architecture.
