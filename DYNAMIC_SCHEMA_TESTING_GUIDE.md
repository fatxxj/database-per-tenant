# Dynamic Schema Testing Guide

This guide will show you how to test your dynamic schema system step by step.

## 🚀 **Step 1: Start the Service**

The service should now be running on `https://localhost:7001` (or similar). You can access Swagger UI at:
```
https://localhost:7001/swagger
```

## 🧪 **Step 2: Test the Dynamic Schema System**

### **2.1 Create a Development Token**

First, you need a JWT token to authenticate your requests:

```bash
curl -X POST "https://localhost:7001/auth/dev-token" \
  -H "Content-Type: application/json" \
  -d '{
    "tenantId": "my-ecommerce-app"
  }'
```

**Expected Response:**
```json
{
  "token": "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9..."
}
```

### **2.2 Create a Tenant with Dynamic Schema**

Now create a tenant with a custom e-commerce schema:

```bash
curl -X POST "https://localhost:7001/tenants" \
  -H "Content-Type: application/json" \
  -d '{
    "name": "my-ecommerce-app",
    "schemaDefinition": {
      "version": "1.0",
      "name": "E-commerce Schema",
      "description": "Complete e-commerce database schema",
      "tables": [
        {
          "name": "Products",
          "description": "Product catalog",
          "columns": [
            {
              "name": "Id",
              "dataType": "nvarchar",
              "maxLength": 50,
              "isPrimaryKey": true,
              "isNullable": false,
              "description": "Unique product identifier"
            },
            {
              "name": "Name",
              "dataType": "nvarchar",
              "maxLength": 200,
              "isNullable": false,
              "description": "Product name"
            },
            {
              "name": "Price",
              "dataType": "decimal",
              "precision": 18,
              "scale": 2,
              "isNullable": false,
              "description": "Product price"
            },
            {
              "name": "CategoryId",
              "dataType": "nvarchar",
              "maxLength": 50,
              "isNullable": false,
              "description": "Category reference"
            },
            {
              "name": "IsActive",
              "dataType": "bit",
              "isNullable": false,
              "defaultValue": "1",
              "description": "Product availability status"
            },
            {
              "name": "CreatedAt",
              "dataType": "datetime2",
              "isNullable": false,
              "defaultValue": "GETUTCDATE()",
              "description": "Creation timestamp"
            }
          ],
          "indexes": [
            {
              "name": "IX_Products_CategoryId",
              "columns": ["CategoryId"],
              "isUnique": false,
              "description": "Index for category lookups"
            },
            {
              "name": "IX_Products_Name",
              "columns": ["Name"],
              "isUnique": false,
              "description": "Index for product name searches"
            }
          ]
        },
        {
          "name": "Categories",
          "description": "Product categories",
          "columns": [
            {
              "name": "Id",
              "dataType": "nvarchar",
              "maxLength": 50,
              "isPrimaryKey": true,
              "isNullable": false,
              "description": "Unique category identifier"
            },
            {
              "name": "Name",
              "dataType": "nvarchar",
              "maxLength": 100,
              "isNullable": false,
              "description": "Category name"
            },
            {
              "name": "Description",
              "dataType": "nvarchar",
              "maxLength": 500,
              "isNullable": true,
              "description": "Category description"
            },
            {
              "name": "IsActive",
              "dataType": "bit",
              "isNullable": false,
              "defaultValue": "1",
              "description": "Category availability status"
            },
            {
              "name": "CreatedAt",
              "dataType": "datetime2",
              "isNullable": false,
              "defaultValue": "GETUTCDATE()",
              "description": "Creation timestamp"
            }
          ],
          "indexes": [
            {
              "name": "IX_Categories_Name",
              "columns": ["Name"],
              "isUnique": false,
              "description": "Index for category name searches"
            }
          ]
        }
      ],
      "collections": [
        {
          "name": "product_reviews",
          "description": "Product reviews and ratings",
          "indexes": [
            {
              "name": "IX_product_reviews_product_id",
              "columns": ["product_id"],
              "isUnique": false,
              "description": "Index for product review lookups"
            },
            {
              "name": "IX_product_reviews_rating",
              "columns": ["rating"],
              "isUnique": false,
              "description": "Index for rating-based queries"
            }
          ]
        }
      ]
    }
  }'
```

**Expected Response:**
```json
{
  "tenantId": "abc123def456"
}
```

### **2.3 Test Dynamic Data Access**

Now you can perform CRUD operations on your custom tables:

#### **Insert Data into Products Table**

```bash
curl -X POST "https://localhost:7001/api/data/tables/Products" \
  -H "Authorization: Bearer YOUR_JWT_TOKEN" \
  -H "Content-Type: application/json" \
  -d '{
    "Id": "prod-001",
    "Name": "Gaming Laptop",
    "Price": 1299.99,
    "CategoryId": "cat-001",
    "IsActive": true
  }'
```

#### **Insert Data into Categories Table**

```bash
curl -X POST "https://localhost:7001/api/data/tables/Categories" \
  -H "Authorization: Bearer YOUR_JWT_TOKEN" \
  -H "Content-Type: application/json" \
  -d '{
    "Id": "cat-001",
    "Name": "Electronics",
    "Description": "Electronic devices and gadgets",
    "IsActive": true
  }'
```

#### **Query Products with Filters**

```bash
curl -X GET "https://localhost:7001/api/data/tables/Products?where=Price>1000&orderBy=Price DESC" \
  -H "Authorization: Bearer YOUR_JWT_TOKEN"
```

#### **Get Product by ID**

```bash
curl -X GET "https://localhost:7001/api/data/tables/Products/prod-001" \
  -H "Authorization: Bearer YOUR_JWT_TOKEN"
```

#### **Update Product**

```bash
curl -X PUT "https://localhost:7001/api/data/tables/Products/prod-001" \
  -H "Authorization: Bearer YOUR_JWT_TOKEN" \
  -H "Content-Type: application/json" \
  -d '{
    "Price": 1199.99,
    "Name": "Gaming Laptop Pro"
  }'
```

#### **Delete Product**

```bash
curl -X DELETE "https://localhost:7001/api/data/tables/Products/prod-001" \
  -H "Authorization: Bearer YOUR_JWT_TOKEN"
```

### **2.4 Test MongoDB Collections**

#### **Insert Document into MongoDB Collection**

```bash
curl -X POST "https://localhost:7001/api/data/collections/product_reviews" \
  -H "Authorization: Bearer YOUR_JWT_TOKEN" \
  -H "Content-Type: application/json" \
  -d '{
    "product_id": "prod-001",
    "user_id": "user-123",
    "rating": 5,
    "comment": "Excellent gaming performance!",
    "created_at": "2024-01-15T10:30:00Z"
  }'
```

#### **Query MongoDB Collection**

```bash
curl -X GET "https://localhost:7001/api/data/collections/product_reviews?filter={\"product_id\": \"prod-001\"}&sort={\"created_at\": -1}" \
  -H "Authorization: Bearer YOUR_JWT_TOKEN"
```

### **2.5 Schema Management**

#### **Get Tenant Schema**

```bash
curl -X GET "https://localhost:7001/tenants/abc123def456/schema" \
  -H "Authorization: Bearer YOUR_JWT_TOKEN"
```

#### **Update Schema**

```bash
curl -X POST "https://localhost:7001/tenants/abc123def456/schema" \
  -H "Authorization: Bearer YOUR_JWT_TOKEN" \
  -H "Content-Type: application/json" \
  -d '{
    "schemaDefinition": "{\"version\": \"1.1\", \"name\": \"Updated E-commerce Schema\", \"tables\": [...], \"collections\": [...]}"
  }'
```

#### **Validate Schema**

```bash
curl -X POST "https://localhost:7001/schema/validate" \
  -H "Content-Type: application/json" \
  -d '{
    "schemaDefinition": {
      "version": "1.0",
      "name": "Test Schema",
      "tables": [
        {
          "name": "TestTable",
          "columns": [
            {
              "name": "Id",
              "dataType": "nvarchar",
              "maxLength": 50,
              "isPrimaryKey": true,
              "isNullable": false
            }
          ]
        }
      ]
    }
  }'
```

## 🎯 **Step 3: Complete Testing Scenario**

Here's a complete testing scenario you can run:

### **Scenario: E-commerce Application**

1. **Create Token**
2. **Create Tenant with Schema**
3. **Insert Categories**
4. **Insert Products**
5. **Insert Reviews (MongoDB)**
6. **Query and Filter Data**
7. **Update Records**
8. **Delete Records**

### **PowerShell Script for Testing**

```powershell
# Set base URL
$baseUrl = "https://localhost:7001"

# Step 1: Create token
$tokenResponse = Invoke-RestMethod -Uri "$baseUrl/auth/dev-token" -Method POST -ContentType "application/json" -Body '{"tenantId": "ecommerce-test"}'
$token = $tokenResponse.token
$headers = @{ "Authorization" = "Bearer $token"; "Content-Type" = "application/json" }

Write-Host "Token created: $token"

# Step 2: Create tenant with schema
$schema = Get-Content "sample-schemas/ecommerce-schema.json" -Raw
$tenantResponse = Invoke-RestMethod -Uri "$baseUrl/tenants" -Method POST -Headers $headers -Body "{\"name\": \"ecommerce-test\", \"schemaDefinition\": $schema}"
$tenantId = $tenantResponse.tenantId

Write-Host "Tenant created: $tenantId"

# Step 3: Insert category
$categoryData = @{
    Id = "cat-001"
    Name = "Electronics"
    Description = "Electronic devices"
    IsActive = $true
} | ConvertTo-Json

Invoke-RestMethod -Uri "$baseUrl/api/data/tables/Categories" -Method POST -Headers $headers -Body $categoryData
Write-Host "Category inserted"

# Step 4: Insert product
$productData = @{
    Id = "prod-001"
    Name = "Gaming Laptop"
    Price = 1299.99
    CategoryId = "cat-001"
    IsActive = $true
} | ConvertTo-Json

Invoke-RestMethod -Uri "$baseUrl/api/data/tables/Products" -Method POST -Headers $headers -Body $productData
Write-Host "Product inserted"

# Step 5: Query products
$products = Invoke-RestMethod -Uri "$baseUrl/api/data/tables/Products" -Method GET -Headers $headers
Write-Host "Products found: $($products.Count)"

# Step 6: Insert MongoDB review
$reviewData = @{
    product_id = "prod-001"
    user_id = "user-123"
    rating = 5
    comment = "Great laptop!"
    created_at = (Get-Date).ToString("yyyy-MM-ddTHH:mm:ssZ")
} | ConvertTo-Json

Invoke-RestMethod -Uri "$baseUrl/api/data/collections/product_reviews" -Method POST -Headers $headers -Body $reviewData
Write-Host "Review inserted"

# Step 7: Query reviews
$reviews = Invoke-RestMethod -Uri "$baseUrl/api/data/collections/product_reviews" -Method GET -Headers $headers
Write-Host "Reviews found: $($reviews.Count)"

Write-Host "Testing completed successfully!"
```

## 🔍 **Step 4: Verify Database Creation**

You can verify that the dynamic schema was created correctly by:

1. **Check SQL Server**: Connect to your SQL Server instance and look for databases named `tenant_{tenantId}`
2. **Check MongoDB**: Connect to MongoDB and look for databases named `tenant_{tenantId}`
3. **Use Schema Introspection**: Call the schema endpoints to see what was created

## 🎉 **Expected Results**

After running these tests, you should see:

✅ **Dynamic tables created** in SQL Server with your custom schema  
✅ **MongoDB collections created** with indexes  
✅ **CRUD operations working** on any table/collection  
✅ **Schema validation** preventing invalid schemas  
✅ **Tenant isolation** ensuring data separation  

## 🚨 **Troubleshooting**

### **Common Issues:**

1. **Connection Errors**: Make sure SQL Server and MongoDB are running
2. **Authentication Errors**: Ensure JWT token is valid and not expired
3. **Schema Validation Errors**: Check your JSON schema format
4. **Database Creation Errors**: Verify database permissions

### **Debug Tips:**

1. Check the service logs for detailed error messages
2. Use Swagger UI for interactive testing
3. Verify connection strings in `appsettings.json`
4. Test with simple schemas first

This testing guide will help you verify that your dynamic schema system is working correctly and can handle any SaaS application's database needs!
