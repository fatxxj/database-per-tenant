using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Data.SqlClient;
using MongoDB.Driver;
using Dapper;

namespace TenantDbService.Api.Common;

public class DynamicSchemaService
{
    private readonly ILogger<DynamicSchemaService> _logger;

    public DynamicSchemaService(ILogger<DynamicSchemaService> logger)
    {
        _logger = logger;
    }

    public async Task CreateSchemaAsync(SqlConnection connection, SchemaDefinition schema)
    {
        _logger.LogInformation("Creating dynamic schema for: {SchemaName}", schema.Name);

        // Create tables
        foreach (var table in schema.Tables)
        {
            await CreateTableAsync(connection, table);
        }

        foreach (var table in schema.Tables)
        {
            foreach (var foreignKey in table.ForeignKeys)
            {
                await CreateForeignKeyAsync(connection, table.Name, foreignKey);
            }
        }

        _logger.LogInformation("Schema creation completed for: {SchemaName}", schema.Name);
    }

    public async Task CreateTableAsync(System.Data.IDbConnection connection, TableDefinition table)
    {
        var createTableSql = GenerateCreateTableSql(table);
        await connection.ExecuteAsync(createTableSql);
        
        foreach (var index in table.Indexes)
        {
            var createIndexSql = GenerateCreateIndexSql(table.Name, index);
            await connection.ExecuteAsync(createIndexSql);
        }

        _logger.LogDebug("Created table: {TableName}", table.Name);
    }

    private async Task CreateForeignKeyAsync(SqlConnection connection, string tableName, ForeignKeyDefinition foreignKey)
    {
        var createForeignKeySql = GenerateCreateForeignKeySql(tableName, foreignKey);
        await connection.ExecuteAsync(createForeignKeySql);
        
        _logger.LogDebug("Created foreign key: {ForeignKeyName} on table: {TableName}", foreignKey.Name, tableName);
    }

    private string GenerateCreateTableSql(TableDefinition table)
    {
        var columns = table.Columns.Select(c => GenerateColumnDefinition(c));
        var primaryKeyColumns = table.Columns.Where(c => c.IsPrimaryKey).Select(c => $"[{c.Name}]");
        
        var sql = new StringBuilder();
        sql.AppendLine($"IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = '{table.Name}')");
        sql.AppendLine("BEGIN");
        sql.AppendLine($"    CREATE TABLE [{table.Name}] (");
        sql.AppendLine($"        {string.Join(",\n        ", columns)}");
        
        if (primaryKeyColumns.Any())
        {
            sql.AppendLine($"        ,PRIMARY KEY ({string.Join(", ", primaryKeyColumns)})");
        }
        
        sql.AppendLine("    )");
        sql.AppendLine("END");

        return sql.ToString();
    }

    private string GenerateColumnDefinition(ColumnDefinition column)
    {
        var columnDef = new StringBuilder();
        columnDef.Append($"[{column.Name}] {GetSqlDataType(column)}");
        
        if (!column.IsNullable)
        {
            columnDef.Append(" NOT NULL");
        }
        
        if (column.IsIdentity)
        {
            columnDef.Append(" IDENTITY(1,1)");
        }
        
        if (!string.IsNullOrEmpty(column.DefaultValue))
        {
            columnDef.Append($" DEFAULT {column.DefaultValue}");
        }

        return columnDef.ToString();
    }

    private string GetSqlDataType(ColumnDefinition column)
    {
        return column.DataType.ToLower() switch
        {
            "string" or "varchar" => column.MaxLength.HasValue ? $"VARCHAR({column.MaxLength})" : "VARCHAR(255)",
            "nvarchar" => column.MaxLength.HasValue ? $"NVARCHAR({column.MaxLength})" : "NVARCHAR(255)",
            "text" => "TEXT",
            "ntext" => "NTEXT",
            "int" => "INT",
            "bigint" => "BIGINT",
            "smallint" => "SMALLINT",
            "tinyint" => "TINYINT",
            "decimal" => column.Precision.HasValue && column.Scale.HasValue 
                ? $"DECIMAL({column.Precision},{column.Scale})" 
                : "DECIMAL(18,2)",
            "money" => "MONEY",
            "float" => "FLOAT",
            "real" => "REAL",
            "bit" => "BIT",
            "datetime" => "DATETIME",
            "datetime2" => "DATETIME2",
            "date" => "DATE",
            "time" => "TIME",
            "timestamp" => "TIMESTAMP",
            "uniqueidentifier" => "UNIQUEIDENTIFIER",
            "binary" => column.MaxLength.HasValue ? $"BINARY({column.MaxLength})" : "BINARY(1)",
            "varbinary" => column.MaxLength.HasValue ? $"VARBINARY({column.MaxLength})" : "VARBINARY(MAX)",
            "image" => "IMAGE",
            _ => throw new ArgumentException($"Unsupported data type: {column.DataType}")
        };
    }

    private string GenerateCreateIndexSql(string tableName, IndexDefinition index)
    {
        var sql = new StringBuilder();
        sql.AppendLine($"IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = '{index.Name}' AND object_id = OBJECT_ID('{tableName}'))");
        sql.AppendLine("BEGIN");
        
        var indexType = index.IsUnique ? "UNIQUE" : "";
        var clustered = index.IsClustered ? "CLUSTERED" : "NONCLUSTERED";
        var columns = string.Join(", ", index.Columns.Select(c => $"[{c}]"));
        
        sql.AppendLine($"    CREATE {indexType} {clustered} INDEX [{index.Name}] ON [{tableName}] ({columns})");
        sql.AppendLine("END");

        return sql.ToString();
    }

    private string GenerateCreateForeignKeySql(string tableName, ForeignKeyDefinition foreignKey)
    {
        var sql = new StringBuilder();
        sql.AppendLine($"IF NOT EXISTS (SELECT * FROM sys.foreign_keys WHERE name = '{foreignKey.Name}')");
        sql.AppendLine("BEGIN");
        
        var columns = string.Join(", ", foreignKey.Columns.Select(c => $"[{c}]"));
        var referencedColumns = string.Join(", ", foreignKey.ReferencedColumns.Select(c => $"[{c}]"));
        
        sql.AppendLine($"    ALTER TABLE [{tableName}] ADD CONSTRAINT [{foreignKey.Name}]");
        sql.AppendLine($"    FOREIGN KEY ({columns}) REFERENCES [{foreignKey.ReferencedTable}] ({referencedColumns})");
        sql.AppendLine("END");

        return sql.ToString();
    }

    public async Task CreateMongoCollectionsAsync(IMongoDatabase database, SchemaDefinition schema)
    {
        _logger.LogInformation("Creating MongoDB collections for schema: {SchemaName}", schema.Name);

        foreach (var collection in schema.Collections)
        {
            await CreateMongoCollectionAsync(database, collection);
        }
    }

    private async Task CreateMongoCollectionAsync(IMongoDatabase database, CollectionDefinition collection)
    {
        var collectionName = collection.Name;
        
        var existingCollections = await (await database.ListCollectionNamesAsync()).ToListAsync();
        if (!existingCollections.Contains(collectionName))
        {
            await database.CreateCollectionAsync(collectionName);
            _logger.LogDebug("Explicitly created MongoDB collection: {CollectionName}", collectionName);
        }
        
        var mongoCollection = database.GetCollection<MongoDB.Bson.BsonDocument>(collectionName);

        foreach (var index in collection.Indexes)
        {
            await CreateMongoIndexAsync(mongoCollection, index);
        }

        _logger.LogDebug("Created MongoDB collection with indexes: {CollectionName}", collectionName);
    }

    private async Task CreateMongoIndexAsync(IMongoCollection<MongoDB.Bson.BsonDocument> collection, IndexDefinition index)
    {
        var indexKeysDefinition = Builders<MongoDB.Bson.BsonDocument>.IndexKeys;
        
        // Build index keys based on columns
        var keys = new List<IndexKeysDefinition<MongoDB.Bson.BsonDocument>>();
        foreach (var column in index.Columns)
        {
            keys.Add(indexKeysDefinition.Ascending(column));
        }
        
        var combinedKeys = indexKeysDefinition.Combine(keys);
        
        var indexOptions = new CreateIndexOptions
        {
            Name = index.Name,
            Unique = index.IsUnique
        };
        
        var indexModel = new CreateIndexModel<MongoDB.Bson.BsonDocument>(combinedKeys, indexOptions);
        await collection.Indexes.CreateOneAsync(indexModel);
        
        _logger.LogDebug("Created MongoDB index: {IndexName} on collection", index.Name);
    }

    public SchemaValidationResponse ValidateSchema(SchemaDefinition schema)
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(schema.Name))
        {
            errors.Add("Schema name is required");
        }

        var tableNames = new HashSet<string>();
        foreach (var table in schema.Tables)
        {
            if (!tableNames.Add(table.Name))
            {
                errors.Add($"Duplicate table name: {table.Name}");
                continue;
            }

            var tableErrors = ValidateTable(table);
            errors.AddRange(tableErrors.Select(e => $"Table '{table.Name}': {e}"));
        }

        foreach (var table in schema.Tables)
        {
            foreach (var foreignKey in table.ForeignKeys)
            {
                var fkErrors = ValidateForeignKey(table.Name, foreignKey, schema.Tables);
                errors.AddRange(fkErrors);
            }
        }

        var collectionNames = new HashSet<string>();
        foreach (var collection in schema.Collections)
        {
            if (string.IsNullOrWhiteSpace(collection.Name))
            {
                errors.Add("Collection name is required");
                continue;
            }

            if (!collectionNames.Add(collection.Name))
            {
                errors.Add($"Duplicate collection name: {collection.Name}");
            }
        }

        return new SchemaValidationResponse(errors.Count == 0, errors);
    }

    private List<string> ValidateTable(TableDefinition table)
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(table.Name))
        {
            errors.Add("Table name is required");
        }

        if (!table.Columns.Any())
        {
            errors.Add("At least one column is required");
        }

        var columnNames = new HashSet<string>();
        var hasPrimaryKey = false;

        foreach (var column in table.Columns)
        {
            if (!columnNames.Add(column.Name))
            {
                errors.Add($"Duplicate column name: {column.Name}");
                continue;
            }

            if (column.IsPrimaryKey)
            {
                hasPrimaryKey = true;
            }

            var columnErrors = ValidateColumn(column);
            errors.AddRange(columnErrors.Select(e => $"Column '{column.Name}': {e}"));
        }

        if (!hasPrimaryKey)
        {
            errors.Add("At least one primary key column is required");
        }

        return errors;
    }

    private List<string> ValidateColumn(ColumnDefinition column)
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(column.Name))
        {
            errors.Add("Column name is required");
        }

        if (string.IsNullOrWhiteSpace(column.DataType))
        {
            errors.Add("Data type is required");
        }

        switch (column.DataType.ToLower())
        {
            case "varchar":
            case "nvarchar":
            case "string":
                if (!column.MaxLength.HasValue || column.MaxLength <= 0)
                {
                    errors.Add("MaxLength is required for string types");
                }
                break;
            case "decimal":
                if (!column.Precision.HasValue || !column.Scale.HasValue)
                {
                    errors.Add("Precision and Scale are required for decimal type");
                }
                break;
        }

        return errors;
    }

    private List<string> ValidateForeignKey(string tableName, ForeignKeyDefinition foreignKey, List<TableDefinition> allTables)
    {
        var errors = new List<string>();

        var referencedTable = allTables.FirstOrDefault(t => t.Name == foreignKey.ReferencedTable);
        if (referencedTable == null)
        {
            errors.Add($"Referenced table '{foreignKey.ReferencedTable}' does not exist");
            return errors;
        }

        var sourceTable = allTables.FirstOrDefault(t => t.Name == tableName);
        if (sourceTable != null)
        {
            foreach (var column in foreignKey.Columns)
            {
                if (!sourceTable.Columns.Any(c => c.Name == column))
                {
                    errors.Add($"Column '{column}' does not exist in table '{tableName}'");
                }
            }
        }

        foreach (var column in foreignKey.ReferencedColumns)
        {
            if (!referencedTable.Columns.Any(c => c.Name == column))
            {
                errors.Add($"Referenced column '{column}' does not exist in table '{foreignKey.ReferencedTable}'");
            }
        }

        if (foreignKey.Columns.Count != foreignKey.ReferencedColumns.Count)
        {
            errors.Add("Number of columns must match number of referenced columns");
        }

        return errors;
    }
}
