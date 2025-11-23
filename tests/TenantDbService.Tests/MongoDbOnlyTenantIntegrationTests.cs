using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Driver;
using TenantDbService.Api.Catalog;
using TenantDbService.Api.Common;
using TenantDbService.Api.Data.Mongo;
using TenantDbService.Api.Provisioning;
using Xunit;

namespace TenantDbService.Tests;

public class MongoDbOnlyTenantIntegrationTests : IClassFixture<TestDatabaseFixture>, IDisposable
{
    private readonly TestDatabaseFixture _fixture;
    private readonly IServiceProvider _serviceProvider;
    private string? _testTenantId;

    public MongoDbOnlyTenantIntegrationTests(TestDatabaseFixture fixture)
    {
        _fixture = fixture;
        _serviceProvider = _fixture.ServiceProvider;
    }
    
    private T GetService<T>() where T : notnull
    {
        return _serviceProvider.GetRequiredService<T>();
    }

    [Fact]
    public async Task CreateMongoDbOnlyTenant_ShouldSucceed()
    {
        // Arrange
        var tenantName = $"test-mongo-tenant-{Guid.NewGuid():N}";
        var provisioningService = GetService<ProvisioningService>();
        var catalogRepository = GetService<ICatalogRepository>();

        // Act
        _testTenantId = await provisioningService.CreateTenantAsync(tenantName, DatabaseType.MongoDb);

        // Assert
        _testTenantId.Should().NotBeNullOrEmpty();
        
        var tenant = await catalogRepository.GetTenantAsync(_testTenantId);
        tenant.Should().NotBeNull();
        tenant!.Name.Should().Be(tenantName);
        tenant.DatabaseType.Should().Be(DatabaseType.MongoDb);
        tenant.Status.Should().Be(Constants.TenantStatus.Active);

        var connections = await catalogRepository.GetConnectionsAsync(_testTenantId);
        connections.Should().NotBeNull();
        connections!.MongoDbConnectionString.Should().NotBeNullOrEmpty();
        connections.MongoDbDatabaseName.Should().NotBeNullOrEmpty();
        connections.SqlServerConnectionString.Should().BeNull();
    }

    [Fact]
    public async Task CreateTenantWithSchema_ShouldCreateCollections()
    {
        // Arrange
        var tenantName = $"test-mongo-schema-{Guid.NewGuid():N}";
        var schema = CreateContentManagementSchema();
        var provisioningService = GetService<ProvisioningService>();

        // Act
        _testTenantId = await provisioningService.CreateTenantAsync(tenantName, DatabaseType.MongoDb, schema);
        
        await TestHelpers.SetupTenantContextAsync(_serviceProvider, _testTenantId);
        
        using var scope = _serviceProvider.CreateScope();

        // Assert
        _testTenantId.Should().NotBeNullOrEmpty();

        var dataService = scope.ServiceProvider.GetRequiredService<DynamicDataService>();
        var collections = await dataService.GetCollectionNamesAsync();
        collections.Should().Contain("articles");
        collections.Should().Contain("comments");
        collections.Should().Contain("tags");
    }

    [Fact]
    public async Task CreateSchemaWithIndexes_ShouldCreateIndexes()
    {
        // Arrange
        var tenantName = $"test-mongo-indexes-{Guid.NewGuid():N}";
        var schema = CreateContentManagementSchema();
        var provisioningService = GetService<ProvisioningService>();

        // Act
        _testTenantId = await provisioningService.CreateTenantAsync(tenantName, DatabaseType.MongoDb, schema);
        await TestHelpers.SetupTenantContextAsync(_serviceProvider, _testTenantId);

        // Assert
        var mongoFactory = GetService<IMongoDbFactory>();
        var database = await mongoFactory.GetDatabaseAsync();
        var articlesCollection = database.GetCollection<MongoDB.Bson.BsonDocument>("articles");
        
        var indexes = await articlesCollection.Indexes.ListAsync();
        var indexList = await indexes.ToListAsync();
        
        var indexNames = indexList.Select(idx => idx["name"].AsString).ToList();
        indexNames.Should().Contain("IX_articles_slug");
        indexNames.Should().Contain("IX_articles_authorId");
        indexNames.Should().Contain("IX_articles_publishedAt");
    }

    [Fact]
    public async Task InsertDocument_ShouldSucceed()
    {
        // Arrange
        var tenantName = $"test-mongo-insert-{Guid.NewGuid():N}";
        var schema = CreateContentManagementSchema();
        var provisioningService = GetService<ProvisioningService>();
        _testTenantId = await provisioningService.CreateTenantAsync(tenantName, DatabaseType.MongoDb, schema);
        await TestHelpers.SetupTenantContextAsync(_serviceProvider, _testTenantId);

        var dataService = GetService<DynamicDataService>();
        var articleData = new Dictionary<string, object>
        {
            ["_id"] = Guid.NewGuid().ToString(),
            ["title"] = "Test Article",
            ["slug"] = "test-article",
            ["content"] = "This is a test article content.",
            ["authorId"] = "author-123",
            ["publishedAt"] = DateTime.UtcNow,
            ["tags"] = new[] { "test", "mongodb" },
            ["metadata"] = new Dictionary<string, object>
            {
                ["views"] = 0,
                ["likes"] = 0
            }
        };

        // Act
        var articleId = await dataService.InsertMongoAsync("articles", articleData);

        // Assert
        articleId.Should().NotBeNullOrEmpty();

        var inserted = await dataService.GetMongoByIdAsync("articles", articleId);
        inserted.Should().NotBeNull();
        inserted!["title"].Should().Be("Test Article");
        inserted["slug"].Should().Be("test-article");
    }

    [Fact]
    public async Task InsertMultipleDocuments_ShouldSucceed()
    {
        // Arrange
        var tenantName = $"test-mongo-insert-multi-{Guid.NewGuid():N}";
        var schema = CreateContentManagementSchema();
        var provisioningService = GetService<ProvisioningService>();
        _testTenantId = await provisioningService.CreateTenantAsync(tenantName, DatabaseType.MongoDb, schema);
        await TestHelpers.SetupTenantContextAsync(_serviceProvider, _testTenantId);

        var dataService = GetService<DynamicDataService>();
        var article1Id = await dataService.InsertMongoAsync("articles", new Dictionary<string, object>
        {
            ["_id"] = Guid.NewGuid().ToString(),
            ["title"] = "Article One",
            ["slug"] = "article-one",
            ["content"] = "Content one",
            ["authorId"] = "author-1",
            ["publishedAt"] = DateTime.UtcNow,
            ["tags"] = new[] { "tech" }
        });

        var article2Id = await dataService.InsertMongoAsync("articles", new Dictionary<string, object>
        {
            ["_id"] = Guid.NewGuid().ToString(),
            ["title"] = "Article Two",
            ["slug"] = "article-two",
            ["content"] = "Content two",
            ["authorId"] = "author-2",
            ["publishedAt"] = DateTime.UtcNow,
            ["tags"] = new[] { "science" }
        });

        // Act
        var articles = await dataService.QueryMongoAsync("articles", limit: 10);

        // Assert
        articles.Should().HaveCount(2);
        articles.Should().Contain(a => a["slug"].ToString() == "article-one");
        articles.Should().Contain(a => a["slug"].ToString() == "article-two");
    }

    [Fact]
    public async Task QueryWithFilters_ShouldReturnFilteredResults()
    {
        // Arrange
        var tenantName = $"test-mongo-query-{Guid.NewGuid():N}";
        var schema = CreateContentManagementSchema();
        var provisioningService = GetService<ProvisioningService>();
        _testTenantId = await provisioningService.CreateTenantAsync(tenantName, DatabaseType.MongoDb, schema);
        await TestHelpers.SetupTenantContextAsync(_serviceProvider, _testTenantId);

        var dataService = GetService<DynamicDataService>();
        await dataService.InsertMongoAsync("articles", new Dictionary<string, object>
        {
            ["_id"] = Guid.NewGuid().ToString(),
            ["title"] = "Filtered Article",
            ["slug"] = "filtered-article",
            ["content"] = "Content",
            ["authorId"] = "author-filter",
            ["publishedAt"] = DateTime.UtcNow,
            ["tags"] = new[] { "filtered" }
        });

        // Act
        var filter = "{\"authorId\":\"author-filter\"}";
        var results = await dataService.QueryMongoAsync("articles", filter: filter, limit: 10);

        // Assert
        results.Should().HaveCount(1);
        results[0]["authorId"].Should().Be("author-filter");
    }

    [Fact]
    public async Task QueryWithSort_ShouldReturnOrderedResults()
    {
        // Arrange
        var tenantName = $"test-mongo-sort-{Guid.NewGuid():N}";
        var schema = CreateContentManagementSchema();
        var provisioningService = GetService<ProvisioningService>();
        _testTenantId = await provisioningService.CreateTenantAsync(tenantName, DatabaseType.MongoDb, schema);
        await TestHelpers.SetupTenantContextAsync(_serviceProvider, _testTenantId);

        var dataService = GetService<DynamicDataService>();
        var now = DateTime.UtcNow;
        
        await dataService.InsertMongoAsync("articles", new Dictionary<string, object>
        {
            ["_id"] = Guid.NewGuid().ToString(),
            ["title"] = "Zebra Article",
            ["slug"] = "zebra-article",
            ["content"] = "Content",
            ["authorId"] = "author-1",
            ["publishedAt"] = now.AddHours(2),
            ["tags"] = new[] { "zebra" }
        });

        await dataService.InsertMongoAsync("articles", new Dictionary<string, object>
        {
            ["_id"] = Guid.NewGuid().ToString(),
            ["title"] = "Alpha Article",
            ["slug"] = "alpha-article",
            ["content"] = "Content",
            ["authorId"] = "author-2",
            ["publishedAt"] = now.AddHours(1),
            ["tags"] = new[] { "alpha" }
        });

        // Act
        var sort = "{\"publishedAt\":1}";
        var results = await dataService.QueryMongoAsync("articles", sort: sort, limit: 10);

        // Assert
        results.Should().HaveCount(2);
        var firstPublished = ((DateTime)results[0]["publishedAt"]).ToUniversalTime();
        var secondPublished = ((DateTime)results[1]["publishedAt"]).ToUniversalTime();
        firstPublished.Should().BeBefore(secondPublished);
    }

    [Fact]
    public async Task UpdateDocument_ShouldSucceed()
    {
        // Arrange
        var tenantName = $"test-mongo-update-{Guid.NewGuid():N}";
        var schema = CreateContentManagementSchema();
        var provisioningService = GetService<ProvisioningService>();
        _testTenantId = await provisioningService.CreateTenantAsync(tenantName, DatabaseType.MongoDb, schema);
        await TestHelpers.SetupTenantContextAsync(_serviceProvider, _testTenantId);

        var dataService = GetService<DynamicDataService>();
        var articleId = await dataService.InsertMongoAsync("articles", new Dictionary<string, object>
        {
            ["_id"] = Guid.NewGuid().ToString(),
            ["title"] = "Original Title",
            ["slug"] = "original-slug",
            ["content"] = "Original content",
            ["authorId"] = "author-1",
            ["publishedAt"] = DateTime.UtcNow,
            ["tags"] = new[] { "original" }
        });

        // Act
        var updated = await dataService.UpdateMongoAsync("articles", articleId, new Dictionary<string, object>
        {
            ["title"] = "Updated Title",
            ["content"] = "Updated content"
        });

        // Assert
        updated.Should().BeTrue();

        var document = await dataService.GetMongoByIdAsync("articles", articleId);
        document.Should().NotBeNull();
        document!["title"].Should().Be("Updated Title");
        document["content"].Should().Be("Updated content");
        document["slug"].Should().Be("original-slug"); // Should remain unchanged
    }

    [Fact]
    public async Task DeleteDocument_ShouldSucceed()
    {
        // Arrange
        var tenantName = $"test-mongo-delete-{Guid.NewGuid():N}";
        var schema = CreateContentManagementSchema();
        var provisioningService = GetService<ProvisioningService>();
        _testTenantId = await provisioningService.CreateTenantAsync(tenantName, DatabaseType.MongoDb, schema);
        await TestHelpers.SetupTenantContextAsync(_serviceProvider, _testTenantId);

        var dataService = GetService<DynamicDataService>();
        var articleId = await dataService.InsertMongoAsync("articles", new Dictionary<string, object>
        {
            ["_id"] = Guid.NewGuid().ToString(),
            ["title"] = "To Delete",
            ["slug"] = "to-delete",
            ["content"] = "Content",
            ["authorId"] = "author-1",
            ["publishedAt"] = DateTime.UtcNow,
            ["tags"] = new[] { "delete" }
        });

        // Act
        var deleted = await dataService.DeleteMongoAsync("articles", articleId);

        // Assert
        deleted.Should().BeTrue();

        var document = await dataService.GetMongoByIdAsync("articles", articleId);
        document.Should().BeNull();
    }

    [Fact]
    public async Task InsertNestedDocument_ShouldSucceed()
    {
        // Arrange
        var tenantName = $"test-mongo-nested-{Guid.NewGuid():N}";
        var schema = CreateContentManagementSchema();
        var provisioningService = GetService<ProvisioningService>();
        _testTenantId = await provisioningService.CreateTenantAsync(tenantName, DatabaseType.MongoDb, schema);
        await TestHelpers.SetupTenantContextAsync(_serviceProvider, _testTenantId);

        var dataService = GetService<DynamicDataService>();
        var articleData = new Dictionary<string, object>
        {
            ["_id"] = Guid.NewGuid().ToString(),
            ["title"] = "Nested Article",
            ["slug"] = "nested-article",
            ["content"] = "Content with nested data",
            ["authorId"] = "author-1",
            ["publishedAt"] = DateTime.UtcNow,
            ["author"] = new Dictionary<string, object>
            {
                ["id"] = "author-1",
                ["name"] = "John Doe",
                ["email"] = "john@example.com",
                ["bio"] = "Author bio"
            },
            ["metadata"] = new Dictionary<string, object>
            {
                ["views"] = 100,
                ["likes"] = 25,
                ["shares"] = 5,
                ["analytics"] = new Dictionary<string, object>
                {
                    ["dailyViews"] = new[] { 10, 15, 20, 25, 30 },
                    ["topCountries"] = new[] { "US", "UK", "CA" }
                }
            },
            ["tags"] = new[] { "nested", "mongodb", "test" }
        };

        // Act
        var articleId = await dataService.InsertMongoAsync("articles", articleData);

        // Assert
        articleId.Should().NotBeNullOrEmpty();

        var document = await dataService.GetMongoByIdAsync("articles", articleId);
        document.Should().NotBeNull();
        
        var author = document!["author"] as Dictionary<string, object>;
        author.Should().NotBeNull();
        author!["name"].Should().Be("John Doe");
        
        var metadata = document["metadata"] as Dictionary<string, object>;
        metadata.Should().NotBeNull();
        metadata!["views"].Should().Be(100);
    }

    [Fact]
    public async Task QueryWithComplexFilters_ShouldReturnFilteredResults()
    {
        // Arrange
        var tenantName = $"test-mongo-complex-{Guid.NewGuid():N}";
        var schema = CreateContentManagementSchema();
        var provisioningService = GetService<ProvisioningService>();
        _testTenantId = await provisioningService.CreateTenantAsync(tenantName, DatabaseType.MongoDb, schema);
        await TestHelpers.SetupTenantContextAsync(_serviceProvider, _testTenantId);

        var dataService = GetService<DynamicDataService>();
        await dataService.InsertMongoAsync("articles", new Dictionary<string, object>
        {
            ["_id"] = Guid.NewGuid().ToString(),
            ["title"] = "High Views Article",
            ["slug"] = "high-views",
            ["content"] = "Content",
            ["authorId"] = "author-1",
            ["publishedAt"] = DateTime.UtcNow,
            ["metadata"] = new Dictionary<string, object>
            {
                ["views"] = 1000
            },
            ["tags"] = new[] { "popular" }
        });

        await dataService.InsertMongoAsync("articles", new Dictionary<string, object>
        {
            ["_id"] = Guid.NewGuid().ToString(),
            ["title"] = "Low Views Article",
            ["slug"] = "low-views",
            ["content"] = "Content",
            ["authorId"] = "author-2",
            ["publishedAt"] = DateTime.UtcNow,
            ["metadata"] = new Dictionary<string, object>
            {
                ["views"] = 10
            },
            ["tags"] = new[] { "new" }
        });

        // Act 
        var filter = "{\"metadata.views\":{\"$gte\":500}}";
        var results = await dataService.QueryMongoAsync("articles", filter: filter, limit: 10);

        // Assert
        results.Should().HaveCount(1);
        results[0]["slug"].Should().Be("high-views");
    }

    [Fact]
    public async Task CreateMultipleSchemas_ShouldSupportMultipleCollectionSets()
    {
        // Arrange
        var tenantName = $"test-mongo-multi-schema-{Guid.NewGuid():N}";
        var provisioningService = GetService<ProvisioningService>();
        
        _testTenantId = await provisioningService.CreateTenantAsync(tenantName, DatabaseType.MongoDb);
        
        using var scope = _serviceProvider.CreateScope();
        var scopedServiceProvider = scope.ServiceProvider;
        
        await TestHelpers.SetupTenantContextAsync(scopedServiceProvider, _testTenantId);

        var mongoFactory = scopedServiceProvider.GetRequiredService<IMongoDbFactory>();
        var schemaService = scopedServiceProvider.GetRequiredService<DynamicSchemaService>();
        var dataService = scopedServiceProvider.GetRequiredService<DynamicDataService>();

        var schema1 = CreateContentManagementSchema();
        schema1.Name = "ContentManagement";
        schema1.Version = "1.0";

        var schema2 = CreateAnalyticsSchema();
        schema2.Name = "Analytics";
        schema2.Version = "1.0";

        var database = await mongoFactory.GetDatabaseAsync();

        // Act
        await schemaService.CreateMongoCollectionsAsync(database, schema1);

        // Apply
        await schemaService.CreateMongoCollectionsAsync(database, schema2);

        // Assert
        var collections = await dataService.GetCollectionNamesAsync();
        collections.Should().Contain("articles");
        collections.Should().Contain("comments");
        collections.Should().Contain("events");
        collections.Should().Contain("metrics");
    }

    private SchemaDefinition CreateContentManagementSchema()
    {
        return new SchemaDefinition
        {
            Name = "ContentManagement",
            Version = "1.0",
            Description = "Content management schema with articles, comments, and tags",
            Collections = new List<CollectionDefinition>
            {
                new CollectionDefinition
                {
                    Name = "articles",
                    Description = "Blog articles and posts",
                    Indexes = new List<IndexDefinition>
                    {
                        new IndexDefinition { Name = "IX_articles_slug", Columns = new List<string> { "slug" }, IsUnique = true },
                        new IndexDefinition { Name = "IX_articles_authorId", Columns = new List<string> { "authorId" } },
                        new IndexDefinition { Name = "IX_articles_publishedAt", Columns = new List<string> { "publishedAt" } }
                    }
                },
                new CollectionDefinition
                {
                    Name = "comments",
                    Description = "Article comments",
                    Indexes = new List<IndexDefinition>
                    {
                        new IndexDefinition { Name = "IX_comments_articleId", Columns = new List<string> { "articleId" } },
                        new IndexDefinition { Name = "IX_comments_createdAt", Columns = new List<string> { "createdAt" } }
                    }
                },
                new CollectionDefinition
                {
                    Name = "tags",
                    Description = "Content tags",
                    Indexes = new List<IndexDefinition>
                    {
                        new IndexDefinition { Name = "IX_tags_name", Columns = new List<string> { "name" }, IsUnique = true }
                    }
                }
            }
        };
    }

    private SchemaDefinition CreateAnalyticsSchema()
    {
        return new SchemaDefinition
        {
            Name = "Analytics",
            Version = "1.0",
            Description = "Analytics schema with events and metrics",
            Collections = new List<CollectionDefinition>
            {
                new CollectionDefinition
                {
                    Name = "events",
                    Description = "User events",
                    Indexes = new List<IndexDefinition>
                    {
                        new IndexDefinition { Name = "IX_events_type", Columns = new List<string> { "type" } },
                        new IndexDefinition { Name = "IX_events_timestamp", Columns = new List<string> { "timestamp" } },
                        new IndexDefinition { Name = "IX_events_userId", Columns = new List<string> { "userId" } }
                    }
                },
                new CollectionDefinition
                {
                    Name = "metrics",
                    Description = "Aggregated metrics",
                    Indexes = new List<IndexDefinition>
                    {
                        new IndexDefinition { Name = "IX_metrics_date", Columns = new List<string> { "date" } },
                        new IndexDefinition { Name = "IX_metrics_category", Columns = new List<string> { "category" } }
                    }
                }
            }
        };
    }

    public void Dispose()
    {
        if (!string.IsNullOrEmpty(_testTenantId))
        {
            try
            {
                var catalogRepository = GetService<ICatalogRepository>();
                catalogRepository.DisableTenantAsync(_testTenantId).GetAwaiter().GetResult();
            }
            catch
            {
            }
        }
    }
}
