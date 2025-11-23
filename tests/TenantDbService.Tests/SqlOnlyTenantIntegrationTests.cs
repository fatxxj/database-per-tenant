using Dapper;
using FluentAssertions;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TenantDbService.Api.Catalog;
using TenantDbService.Api.Catalog.Entities;
using TenantDbService.Api.Common;
using TenantDbService.Api.Data.Sql;
using TenantDbService.Api.Provisioning;
using Xunit;

namespace TenantDbService.Tests;

public class SqlOnlyTenantIntegrationTests : IClassFixture<TestDatabaseFixture>, IDisposable
{
    private readonly TestDatabaseFixture _fixture;
    private readonly IServiceProvider _serviceProvider;
    private readonly ICatalogRepository _catalogRepository;
    private readonly ProvisioningService _provisioningService;
    private readonly DynamicSchemaService _schemaService;
    private readonly DynamicDataService _dataService;
    private readonly SqlConnectionFactory _sqlFactory;
    private string? _testTenantId;

    public SqlOnlyTenantIntegrationTests(TestDatabaseFixture fixture)
    {
        _fixture = fixture;
        _serviceProvider = _fixture.ServiceProvider;
        _catalogRepository = _serviceProvider.GetRequiredService<ICatalogRepository>();
        _provisioningService = _serviceProvider.GetRequiredService<ProvisioningService>();
        _schemaService = _serviceProvider.GetRequiredService<DynamicSchemaService>();
        _dataService = _serviceProvider.GetRequiredService<DynamicDataService>();
        _sqlFactory = _serviceProvider.GetRequiredService<SqlConnectionFactory>();
    }

    [Fact]
    public async Task CreateSqlOnlyTenant_ShouldSucceed()
    {
        // Arrange
        var tenantName = $"test-sql-tenant-{Guid.NewGuid():N}";

        // Act
        _testTenantId = await _provisioningService.CreateTenantAsync(tenantName, DatabaseType.SqlServer);

        // Assert
        _testTenantId.Should().NotBeNullOrEmpty();
        
        var tenant = await _catalogRepository.GetTenantAsync(_testTenantId);
        tenant.Should().NotBeNull();
        tenant!.Name.Should().Be(tenantName);
        tenant.DatabaseType.Should().Be(DatabaseType.SqlServer);
        tenant.Status.Should().Be(Constants.TenantStatus.Active);

        var connections = await _catalogRepository.GetConnectionsAsync(_testTenantId);
        connections.Should().NotBeNull();
        connections!.SqlServerConnectionString.Should().NotBeNullOrEmpty();
        connections.MongoDbConnectionString.Should().BeNull();
    }

    [Fact]
    public async Task CreateTenantWithSchema_ShouldCreateTables()
    {
        // Arrange
        var tenantName = $"test-sql-schema-{Guid.NewGuid():N}";
        var schema = CreateEcommerceSchema();

        // Act
        _testTenantId = await _provisioningService.CreateTenantAsync(tenantName, DatabaseType.SqlServer, schema);

        // Assert
        _testTenantId.Should().NotBeNullOrEmpty();

        await TestHelpers.SetupTenantContextAsync(_serviceProvider, _testTenantId);
        using var connection = await _sqlFactory.CreateConnectionAsync();
        await connection.OpenAsync();

        var tables = await GetTableNamesAsync(connection);
        tables.Should().Contain("Customers");
        tables.Should().Contain("Products");
        tables.Should().Contain("Orders");
        tables.Should().Contain("OrderItems");
    }

    [Fact]
    public async Task CreateSchemaWithIndexes_ShouldCreateIndexes()
    {
        // Arrange
        var tenantName = $"test-sql-indexes-{Guid.NewGuid():N}";
        var schema = CreateEcommerceSchema();

        // Act
        _testTenantId = await _provisioningService.CreateTenantAsync(tenantName, DatabaseType.SqlServer, schema);

        // Assert
        await TestHelpers.SetupTenantContextAsync(_serviceProvider, _testTenantId);
        using var connection = await _sqlFactory.CreateConnectionAsync();
        await connection.OpenAsync();

        var indexes = await GetIndexesAsync(connection, "Customers");
        indexes.Should().Contain(i => i.Contains("IX_Customers_Email", StringComparison.OrdinalIgnoreCase));
        indexes.Should().Contain(i => i.Contains("IX_Customers_Phone", StringComparison.OrdinalIgnoreCase));

        var productIndexes = await GetIndexesAsync(connection, "Products");
        productIndexes.Should().Contain(i => i.Contains("IX_Products_SKU", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task CreateSchemaWithForeignKeys_ShouldCreateForeignKeys()
    {
        // Arrange
        var tenantName = $"test-sql-fkeys-{Guid.NewGuid():N}";
        var schema = CreateEcommerceSchema();

        // Act
        _testTenantId = await _provisioningService.CreateTenantAsync(tenantName, DatabaseType.SqlServer, schema);

        // Assert
        await TestHelpers.SetupTenantContextAsync(_serviceProvider, _testTenantId);
        using var connection = await _sqlFactory.CreateConnectionAsync();
        await connection.OpenAsync();

        var foreignKeys = await GetForeignKeysAsync(connection, "Orders");
        foreignKeys.Should().Contain(fk => fk.Contains("FK_Orders_Customers", StringComparison.OrdinalIgnoreCase));

        var orderItemForeignKeys = await GetForeignKeysAsync(connection, "OrderItems");
        orderItemForeignKeys.Should().Contain(fk => fk.Contains("FK_OrderItems_Orders", StringComparison.OrdinalIgnoreCase));
        orderItemForeignKeys.Should().Contain(fk => fk.Contains("FK_OrderItems_Products", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task InsertData_ShouldSucceed()
    {
        // Arrange
        var tenantName = $"test-sql-insert-{Guid.NewGuid():N}";
        var schema = CreateEcommerceSchema();
        _testTenantId = await _provisioningService.CreateTenantAsync(tenantName, DatabaseType.SqlServer, schema);
        await TestHelpers.SetupTenantContextAsync(_serviceProvider, _testTenantId);

        var customerData = new Dictionary<string, object>
        {
            ["Id"] = Guid.NewGuid().ToString(),
            ["Email"] = "test@example.com",
            ["FirstName"] = "John",
            ["LastName"] = "Doe",
            ["Phone"] = "+1234567890",
            ["CreatedAt"] = DateTime.UtcNow
        };

        // Act
        var customerId = await _dataService.InsertAsync("Customers", customerData);

        // Assert
        customerId.Should().NotBeNullOrEmpty();

        var inserted = await _dataService.GetByIdAsync("Customers", customerId, "Id");
        inserted.Should().NotBeNull();
        inserted!["Email"].Should().Be("test@example.com");
        inserted["FirstName"].Should().Be("John");
    }

    [Fact]
    public async Task InsertMultipleRecords_ShouldSucceed()
    {
        // Arrange
        var tenantName = $"test-sql-insert-multi-{Guid.NewGuid():N}";
        var schema = CreateEcommerceSchema();
        _testTenantId = await _provisioningService.CreateTenantAsync(tenantName, DatabaseType.SqlServer, schema);
        await TestHelpers.SetupTenantContextAsync(_serviceProvider, _testTenantId);

        var customer1Id = await _dataService.InsertAsync("Customers", new Dictionary<string, object>
        {
            ["Id"] = Guid.NewGuid().ToString(),
            ["Email"] = "customer1@example.com",
            ["FirstName"] = "Alice",
            ["LastName"] = "Smith",
            ["Phone"] = "+1111111111",
            ["CreatedAt"] = DateTime.UtcNow
        });

        var customer2Id = await _dataService.InsertAsync("Customers", new Dictionary<string, object>
        {
            ["Id"] = Guid.NewGuid().ToString(),
            ["Email"] = "customer2@example.com",
            ["FirstName"] = "Bob",
            ["LastName"] = "Jones",
            ["Phone"] = "+2222222222",
            ["CreatedAt"] = DateTime.UtcNow
        });

        // Act
        var customers = await _dataService.QueryAsync("Customers", limit: 10);

        // Assert
        customers.Should().HaveCount(2);
        customers.Should().Contain(c => c["Email"].ToString() == "customer1@example.com");
        customers.Should().Contain(c => c["Email"].ToString() == "customer2@example.com");
    }

    [Fact]
    public async Task QueryWithFilters_ShouldReturnFilteredResults()
    {
        // Arrange
        var tenantName = $"test-sql-query-{Guid.NewGuid():N}";
        var schema = CreateEcommerceSchema();
        _testTenantId = await _provisioningService.CreateTenantAsync(tenantName, DatabaseType.SqlServer, schema);
        await TestHelpers.SetupTenantContextAsync(_serviceProvider, _testTenantId);

        await _dataService.InsertAsync("Customers", new Dictionary<string, object>
        {
            ["Id"] = Guid.NewGuid().ToString(),
            ["Email"] = "filtered@example.com",
            ["FirstName"] = "Filter",
            ["LastName"] = "Test",
            ["Phone"] = "+3333333333",
            ["CreatedAt"] = DateTime.UtcNow
        });

        // Act
        var results = await _dataService.QueryAsync("Customers", 
            whereClause: "[Email] = 'filtered@example.com'", 
            limit: 10);

        // Assert
        results.Should().HaveCount(1);
        results[0]["Email"].Should().Be("filtered@example.com");
    }

    [Fact]
    public async Task QueryWithOrderBy_ShouldReturnOrderedResults()
    {
        // Arrange
        var tenantName = $"test-sql-orderby-{Guid.NewGuid():N}";
        var schema = CreateEcommerceSchema();
        _testTenantId = await _provisioningService.CreateTenantAsync(tenantName, DatabaseType.SqlServer, schema);
        await TestHelpers.SetupTenantContextAsync(_serviceProvider, _testTenantId);

        await _dataService.InsertAsync("Customers", new Dictionary<string, object>
        {
            ["Id"] = Guid.NewGuid().ToString(),
            ["Email"] = "zebra@example.com",
            ["FirstName"] = "Zebra",
            ["LastName"] = "Last",
            ["Phone"] = "+1111111111",
            ["CreatedAt"] = DateTime.UtcNow
        });

        await _dataService.InsertAsync("Customers", new Dictionary<string, object>
        {
            ["Id"] = Guid.NewGuid().ToString(),
            ["Email"] = "alpha@example.com",
            ["FirstName"] = "Alpha",
            ["LastName"] = "First",
            ["Phone"] = "+2222222222",
            ["CreatedAt"] = DateTime.UtcNow
        });

        // Act
        var results = await _dataService.QueryAsync("Customers", 
            orderBy: "[Email] ASC", 
            limit: 10);

        // Assert
        results.Should().HaveCount(2);
        results[0]["Email"].Should().Be("alpha@example.com");
        results[1]["Email"].Should().Be("zebra@example.com");
    }

    [Fact]
    public async Task UpdateRecord_ShouldSucceed()
    {
        // Arrange
        var tenantName = $"test-sql-update-{Guid.NewGuid():N}";
        var schema = CreateEcommerceSchema();
        _testTenantId = await _provisioningService.CreateTenantAsync(tenantName, DatabaseType.SqlServer, schema);
        await TestHelpers.SetupTenantContextAsync(_serviceProvider, _testTenantId);

        var customerId = await _dataService.InsertAsync("Customers", new Dictionary<string, object>
        {
            ["Id"] = Guid.NewGuid().ToString(),
            ["Email"] = "original@example.com",
            ["FirstName"] = "Original",
            ["LastName"] = "Name",
            ["Phone"] = "+1111111111",
            ["CreatedAt"] = DateTime.UtcNow
        });

        // Act
        var updated = await _dataService.UpdateAsync("Customers", customerId, new Dictionary<string, object>
        {
            ["Email"] = "updated@example.com",
            ["FirstName"] = "Updated"
        }, "Id");

        // Assert
        updated.Should().BeTrue();

        var record = await _dataService.GetByIdAsync("Customers", customerId, "Id");
        record.Should().NotBeNull();
        record!["Email"].Should().Be("updated@example.com");
        record["FirstName"].Should().Be("Updated");
        record["LastName"].Should().Be("Name"); // Should remain unchanged
    }

    [Fact]
    public async Task DeleteRecord_ShouldSucceed()
    {
        // Arrange
        var tenantName = $"test-sql-delete-{Guid.NewGuid():N}";
        var schema = CreateEcommerceSchema();
        _testTenantId = await _provisioningService.CreateTenantAsync(tenantName, DatabaseType.SqlServer, schema);
        await TestHelpers.SetupTenantContextAsync(_serviceProvider, _testTenantId);

        var customerId = await _dataService.InsertAsync("Customers", new Dictionary<string, object>
        {
            ["Id"] = Guid.NewGuid().ToString(),
            ["Email"] = "todelete@example.com",
            ["FirstName"] = "Delete",
            ["LastName"] = "Me",
            ["Phone"] = "+1111111111",
            ["CreatedAt"] = DateTime.UtcNow
        });

        // Act
        var deleted = await _dataService.DeleteAsync("Customers", customerId, "Id");

        // Assert
        deleted.Should().BeTrue();

        var record = await _dataService.GetByIdAsync("Customers", customerId, "Id");
        record.Should().BeNull();
    }

    [Fact]
    public async Task InsertWithForeignKey_ShouldEnforceReferentialIntegrity()
    {
        // Arrange
        var tenantName = $"test-sql-fk-insert-{Guid.NewGuid():N}";
        var schema = CreateEcommerceSchema();
        _testTenantId = await _provisioningService.CreateTenantAsync(tenantName, DatabaseType.SqlServer, schema);
        await TestHelpers.SetupTenantContextAsync(_serviceProvider, _testTenantId);

        var customerId = Guid.NewGuid().ToString();
        await _dataService.InsertAsync("Customers", new Dictionary<string, object>
        {
            ["Id"] = customerId,
            ["Email"] = "customer@example.com",
            ["FirstName"] = "Customer",
            ["LastName"] = "Name",
            ["Phone"] = "+1111111111",
            ["CreatedAt"] = DateTime.UtcNow
        });

        var productId = Guid.NewGuid().ToString();
        await _dataService.InsertAsync("Products", new Dictionary<string, object>
        {
            ["Id"] = productId,
            ["SKU"] = "PROD-001",
            ["Name"] = "Test Product",
            ["Price"] = 99.99m,
            ["StockQuantity"] = 100,
            ["CreatedAt"] = DateTime.UtcNow
        });

        var orderId = Guid.NewGuid().ToString();
        await _dataService.InsertAsync("Orders", new Dictionary<string, object>
        {
            ["Id"] = orderId,
            ["CustomerId"] = customerId,
            ["OrderDate"] = DateTime.UtcNow,
            ["TotalAmount"] = 199.98m,
            ["Status"] = "Pending",
            ["CreatedAt"] = DateTime.UtcNow
        });

        // Act
        var orderItemId = await _dataService.InsertAsync("OrderItems", new Dictionary<string, object>
        {
            ["Id"] = Guid.NewGuid().ToString(),
            ["OrderId"] = orderId,
            ["ProductId"] = productId,
            ["Quantity"] = 2,
            ["UnitPrice"] = 99.99m,
            ["TotalPrice"] = 199.98m
        });

        // Assert
        orderItemId.Should().NotBeNullOrEmpty();

        var orderItem = await _dataService.GetByIdAsync("OrderItems", orderItemId, "Id");
        orderItem.Should().NotBeNull();
        orderItem!["OrderId"].Should().Be(orderId);
        orderItem["ProductId"].Should().Be(productId);
    }

    [Fact]
    public async Task InsertWithInvalidForeignKey_ShouldFail()
    {
        // Arrange
        var tenantName = $"test-sql-fk-invalid-{Guid.NewGuid():N}";
        var schema = CreateEcommerceSchema();
        _testTenantId = await _provisioningService.CreateTenantAsync(tenantName, DatabaseType.SqlServer, schema);
        await TestHelpers.SetupTenantContextAsync(_serviceProvider, _testTenantId);

        // Act & Assert
        await Assert.ThrowsAsync<SqlException>(async () =>
        {
            await _dataService.InsertAsync("Orders", new Dictionary<string, object>
            {
                ["Id"] = Guid.NewGuid().ToString(),
                ["CustomerId"] = Guid.NewGuid().ToString(), // Non-existent customer
                ["OrderDate"] = DateTime.UtcNow,
                ["TotalAmount"] = 199.98m,
                ["Status"] = "Pending",
                ["CreatedAt"] = DateTime.UtcNow
            });
        });
    }

    [Fact]
    public async Task CreateMultipleSchemas_ShouldSupportMultipleTableSets()
    {
        // Arrange
        var tenantName = $"test-sql-multi-schema-{Guid.NewGuid():N}";
        
        var emptySchema = new SchemaDefinition
        {
            Name = "Empty",
            Version = "1.0",
            Tables = new List<TableDefinition>()
        };
        _testTenantId = await _provisioningService.CreateTenantAsync(tenantName, DatabaseType.SqlServer, emptySchema);
        await TestHelpers.SetupTenantContextAsync(_serviceProvider, _testTenantId);

        var schema1 = CreateEcommerceSchema();
        schema1.Name = "Ecommerce";
        schema1.Version = "1.0";

        var schema2 = CreateBlogSchema();
        schema2.Name = "Blog";
        schema2.Version = "1.0";

        // Act
        using (var connection1 = await _sqlFactory.CreateConnectionAsync())
        {
            await connection1.OpenAsync();
            await _schemaService.CreateSchemaAsync(connection1, schema1);
        }

        using (var connection2 = await _sqlFactory.CreateConnectionAsync())
        {
            await connection2.OpenAsync();
            await _schemaService.CreateSchemaAsync(connection2, schema2);
        
            // Assert
            var tables = await GetTableNamesAsync(connection2);
            tables.Should().Contain("Customers");
            tables.Should().Contain("Products");
            tables.Should().Contain("Posts");
            tables.Should().Contain("Comments");
        }
    }

    private SchemaDefinition CreateEcommerceSchema()
    {
        return new SchemaDefinition
        {
            Name = "Ecommerce",
            Version = "1.0",
            Description = "E-commerce schema with customers, products, orders",
            Tables = new List<TableDefinition>
            {
                new TableDefinition
                {
                    Name = "Customers",
                    Description = "Customer information",
                    Columns = new List<ColumnDefinition>
                    {
                        new ColumnDefinition { Name = "Id", DataType = "nvarchar", MaxLength = 50, IsPrimaryKey = true, IsNullable = false },
                        new ColumnDefinition { Name = "Email", DataType = "nvarchar", MaxLength = 255, IsNullable = false },
                        new ColumnDefinition { Name = "FirstName", DataType = "nvarchar", MaxLength = 100, IsNullable = false },
                        new ColumnDefinition { Name = "LastName", DataType = "nvarchar", MaxLength = 100, IsNullable = false },
                        new ColumnDefinition { Name = "Phone", DataType = "nvarchar", MaxLength = 20, IsNullable = true },
                        new ColumnDefinition { Name = "CreatedAt", DataType = "datetime2", IsNullable = false }
                    },
                    Indexes = new List<IndexDefinition>
                    {
                        new IndexDefinition { Name = "IX_Customers_Email", Columns = new List<string> { "Email" }, IsUnique = true },
                        new IndexDefinition { Name = "IX_Customers_Phone", Columns = new List<string> { "Phone" } }
                    }
                },
                new TableDefinition
                {
                    Name = "Products",
                    Description = "Product catalog",
                    Columns = new List<ColumnDefinition>
                    {
                        new ColumnDefinition { Name = "Id", DataType = "nvarchar", MaxLength = 50, IsPrimaryKey = true, IsNullable = false },
                        new ColumnDefinition { Name = "SKU", DataType = "nvarchar", MaxLength = 100, IsNullable = false },
                        new ColumnDefinition { Name = "Name", DataType = "nvarchar", MaxLength = 255, IsNullable = false },
                        new ColumnDefinition { Name = "Price", DataType = "decimal", Precision = 18, Scale = 2, IsNullable = false },
                        new ColumnDefinition { Name = "StockQuantity", DataType = "int", IsNullable = false },
                        new ColumnDefinition { Name = "CreatedAt", DataType = "datetime2", IsNullable = false }
                    },
                    Indexes = new List<IndexDefinition>
                    {
                        new IndexDefinition { Name = "IX_Products_SKU", Columns = new List<string> { "SKU" }, IsUnique = true }
                    }
                },
                new TableDefinition
                {
                    Name = "Orders",
                    Description = "Customer orders",
                    Columns = new List<ColumnDefinition>
                    {
                        new ColumnDefinition { Name = "Id", DataType = "nvarchar", MaxLength = 50, IsPrimaryKey = true, IsNullable = false },
                        new ColumnDefinition { Name = "CustomerId", DataType = "nvarchar", MaxLength = 50, IsNullable = false },
                        new ColumnDefinition { Name = "OrderDate", DataType = "datetime2", IsNullable = false },
                        new ColumnDefinition { Name = "TotalAmount", DataType = "decimal", Precision = 18, Scale = 2, IsNullable = false },
                        new ColumnDefinition { Name = "Status", DataType = "nvarchar", MaxLength = 50, IsNullable = false },
                        new ColumnDefinition { Name = "CreatedAt", DataType = "datetime2", IsNullable = false }
                    },
                    ForeignKeys = new List<ForeignKeyDefinition>
                    {
                        new ForeignKeyDefinition
                        {
                            Name = "FK_Orders_Customers",
                            ReferencedTable = "Customers",
                            Columns = new List<string> { "CustomerId" },
                            ReferencedColumns = new List<string> { "Id" }
                        }
                    }
                },
                new TableDefinition
                {
                    Name = "OrderItems",
                    Description = "Order line items",
                    Columns = new List<ColumnDefinition>
                    {
                        new ColumnDefinition { Name = "Id", DataType = "nvarchar", MaxLength = 50, IsPrimaryKey = true, IsNullable = false },
                        new ColumnDefinition { Name = "OrderId", DataType = "nvarchar", MaxLength = 50, IsNullable = false },
                        new ColumnDefinition { Name = "ProductId", DataType = "nvarchar", MaxLength = 50, IsNullable = false },
                        new ColumnDefinition { Name = "Quantity", DataType = "int", IsNullable = false },
                        new ColumnDefinition { Name = "UnitPrice", DataType = "decimal", Precision = 18, Scale = 2, IsNullable = false },
                        new ColumnDefinition { Name = "TotalPrice", DataType = "decimal", Precision = 18, Scale = 2, IsNullable = false }
                    },
                    ForeignKeys = new List<ForeignKeyDefinition>
                    {
                        new ForeignKeyDefinition
                        {
                            Name = "FK_OrderItems_Orders",
                            ReferencedTable = "Orders",
                            Columns = new List<string> { "OrderId" },
                            ReferencedColumns = new List<string> { "Id" }
                        },
                        new ForeignKeyDefinition
                        {
                            Name = "FK_OrderItems_Products",
                            ReferencedTable = "Products",
                            Columns = new List<string> { "ProductId" },
                            ReferencedColumns = new List<string> { "Id" }
                        }
                    }
                }
            }
        };
    }

    private SchemaDefinition CreateBlogSchema()
    {
        return new SchemaDefinition
        {
            Name = "Blog",
            Version = "1.0",
            Description = "Blog schema with posts and comments",
            Tables = new List<TableDefinition>
            {
                new TableDefinition
                {
                    Name = "Posts",
                    Description = "Blog posts",
                    Columns = new List<ColumnDefinition>
                    {
                        new ColumnDefinition { Name = "Id", DataType = "nvarchar", MaxLength = 50, IsPrimaryKey = true, IsNullable = false },
                        new ColumnDefinition { Name = "Title", DataType = "nvarchar", MaxLength = 255, IsNullable = false },
                        new ColumnDefinition { Name = "Content", DataType = "ntext", IsNullable = false },
                        new ColumnDefinition { Name = "AuthorId", DataType = "nvarchar", MaxLength = 50, IsNullable = false },
                        new ColumnDefinition { Name = "PublishedAt", DataType = "datetime2", IsNullable = true },
                        new ColumnDefinition { Name = "CreatedAt", DataType = "datetime2", IsNullable = false }
                    }
                },
                new TableDefinition
                {
                    Name = "Comments",
                    Description = "Post comments",
                    Columns = new List<ColumnDefinition>
                    {
                        new ColumnDefinition { Name = "Id", DataType = "nvarchar", MaxLength = 50, IsPrimaryKey = true, IsNullable = false },
                        new ColumnDefinition { Name = "PostId", DataType = "nvarchar", MaxLength = 50, IsNullable = false },
                        new ColumnDefinition { Name = "AuthorName", DataType = "nvarchar", MaxLength = 100, IsNullable = false },
                        new ColumnDefinition { Name = "Content", DataType = "ntext", IsNullable = false },
                        new ColumnDefinition { Name = "CreatedAt", DataType = "datetime2", IsNullable = false }
                    },
                    ForeignKeys = new List<ForeignKeyDefinition>
                    {
                        new ForeignKeyDefinition
                        {
                            Name = "FK_Comments_Posts",
                            ReferencedTable = "Posts",
                            Columns = new List<string> { "PostId" },
                            ReferencedColumns = new List<string> { "Id" }
                        }
                    }
                }
            }
        };
    }

    private async Task<List<string>> GetTableNamesAsync(SqlConnection connection)
    {
        var sql = @"
            SELECT TABLE_NAME 
            FROM INFORMATION_SCHEMA.TABLES 
            WHERE TABLE_TYPE = 'BASE TABLE'
            ORDER BY TABLE_NAME";
        
        var result = await connection.QueryAsync<string>(sql);
        return result.ToList();
    }

    private async Task<List<string>> GetIndexesAsync(SqlConnection connection, string tableName)
    {
        var sql = @"
            SELECT i.name AS IndexName
            FROM sys.indexes i
            INNER JOIN sys.tables t ON i.object_id = t.object_id
            WHERE t.name = @TableName
            AND i.name IS NOT NULL";
        
        var result = await connection.QueryAsync<string>(sql, new { TableName = tableName });
        return result.ToList();
    }

    private async Task<List<string>> GetForeignKeysAsync(SqlConnection connection, string tableName)
    {
        var sql = @"
            SELECT fk.name AS ForeignKeyName
            FROM sys.foreign_keys fk
            INNER JOIN sys.tables t ON fk.parent_object_id = t.object_id
            WHERE t.name = @TableName";
        
        var result = await connection.QueryAsync<string>(sql, new { TableName = tableName });
        return result.ToList();
    }

    public void Dispose()
    {
        if (!string.IsNullOrEmpty(_testTenantId))
        {
            try
            {
                TestDatabaseCleanupHelper.CleanupTenantAsync(
                    _serviceProvider, 
                    _testTenantId, 
                    DatabaseType.SqlServer)
                    .GetAwaiter()
                    .GetResult();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Warning: Failed to cleanup test tenant {_testTenantId}: {ex.Message}");
            }
        }
    }
}

