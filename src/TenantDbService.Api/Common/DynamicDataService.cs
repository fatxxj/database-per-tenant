using Dapper;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;
using System.Data;
using System.Text.Json;
using TenantDbService.Api.Data.Sql;
using TenantDbService.Api.Data.Mongo;
using TenantDbService.Api.Catalog;

namespace TenantDbService.Api.Common;

public class DynamicDataService
{
    private readonly SqlConnectionFactory _sqlFactory;
    private readonly IMongoDbFactory _mongoFactory;
    private readonly ICatalogRepository _catalogRepository;
    private readonly ILogger<DynamicDataService> _logger;

    public DynamicDataService(
        SqlConnectionFactory sqlFactory,
        IMongoDbFactory mongoFactory,
        ICatalogRepository catalogRepository,
        ILogger<DynamicDataService> logger)
    {
        _sqlFactory = sqlFactory;
        _mongoFactory = mongoFactory;
        _catalogRepository = catalogRepository;
        _logger = logger;
    }

    public async Task<List<Dictionary<string, object>>> QueryAsync(string tableName, string? whereClause = null, string? orderBy = null, int? limit = null)
    {
        using var connection = await _sqlFactory.CreateConnectionAsync();
        await connection.OpenAsync();

        whereClause = QuerySanitizer.SanitizeWhereClause(whereClause);
        orderBy = QuerySanitizer.SanitizeOrderBy(orderBy);
        var safeLimit = QuerySanitizer.ValidateLimit(limit);

        var sql = new System.Text.StringBuilder();
        
        if (!string.IsNullOrEmpty(orderBy))
        {
            sql.Append($"SELECT * FROM [{tableName}]");
            
            if (!string.IsNullOrEmpty(whereClause))
            {
                sql.Append($" WHERE {whereClause}");
            }
            
            sql.Append($" ORDER BY {orderBy}");
            sql.Append($" OFFSET 0 ROWS FETCH NEXT {safeLimit} ROWS ONLY");
        }
        else
        {
            sql.Append($"SELECT TOP ({safeLimit}) * FROM [{tableName}]");
            
            if (!string.IsNullOrEmpty(whereClause))
            {
                sql.Append($" WHERE {whereClause}");
            }
        }

        var results = await connection.QueryAsync(sql.ToString());
        return results.Select(r => DapperRowToDictionary(r)).Cast<Dictionary<string, object>>().ToList();
    }

    public async Task<Dictionary<string, object>?> GetByIdAsync(string tableName, string id, string idColumn = "Id")
    {
        using var connection = await _sqlFactory.CreateConnectionAsync();
        await connection.OpenAsync();

        var sql = $"SELECT * FROM [{tableName}] WHERE [{idColumn}] = @Id";
        var result = await connection.QueryFirstOrDefaultAsync(sql, new { Id = id });
        
        return result != null ? DapperRowToDictionary(result) : null;
    }

    public async Task<string> InsertAsync(string tableName, Dictionary<string, object> data)
    {
        _logger.LogInformation("InsertAsync called with tableName: '{TableName}' (length: {Length})", tableName, tableName?.Length ?? 0);
        
        using var connection = await _sqlFactory.CreateConnectionAsync();
        await connection.OpenAsync();

        if (string.IsNullOrWhiteSpace(tableName))
        {
            _logger.LogError("Table name is null or empty");
            throw new ArgumentException("Table name cannot be null or empty", nameof(tableName));
        }

        if (!IsValidIdentifier(tableName))
        {
            _logger.LogError("Invalid table name format: '{TableName}'", tableName);
            throw new ArgumentException(string.Format(Constants.ErrorMessages.InvalidTableName, tableName), nameof(tableName));
        }

        var tableExists = await TableExistsAsync(connection, tableName);
        if (!tableExists)
        {
            _logger.LogError("Table '{TableName}' does not exist in the database", tableName);
            var availableTables = await GetTableNamesAsync();
            _logger.LogInformation("Available tables: {Tables}", string.Join(", ", availableTables));
            throw new ArgumentException(string.Format(Constants.ErrorMessages.TableNotFound, tableName) + 
                $". Available tables: {string.Join(", ", availableTables)}", nameof(tableName));
        }

        var convertedData = ConvertJsonElements(data);

        foreach (var column in convertedData.Keys)
        {
            if (!IsValidIdentifier(column))
            {
                throw new ArgumentException(string.Format(Constants.ErrorMessages.InvalidColumnName, column), nameof(data));
            }
        }

        var columns = convertedData.Keys.Select(k => SanitizeIdentifier(k)).ToList();
        var parameters = convertedData.Keys.Select(k => $"@{k}").ToList();
        
        var sql = $"INSERT INTO {SanitizeIdentifier(tableName)} ({string.Join(", ", columns)}) VALUES ({string.Join(", ", parameters)})";
        
        _logger.LogDebug("Executing INSERT: {Sql}", sql);
        
        await connection.ExecuteAsync(sql, convertedData);
        
        return convertedData.ContainsKey("Id") ? convertedData["Id"].ToString()! : "";
    }

    public async Task<bool> UpdateAsync(string tableName, string id, Dictionary<string, object> data, string idColumn = "Id")
    {
        using var connection = await _sqlFactory.CreateConnectionAsync();
        await connection.OpenAsync();

        var convertedData = ConvertJsonElements(data);

        var setClause = string.Join(", ", convertedData.Keys.Select(k => $"[{k}] = @{k}"));
        var sql = $"UPDATE [{tableName}] SET {setClause} WHERE [{idColumn}] = @Id";
        
        convertedData["Id"] = id;
        var rowsAffected = await connection.ExecuteAsync(sql, convertedData);
        
        return rowsAffected > 0;
    }

    public async Task<bool> DeleteAsync(string tableName, string id, string idColumn = "Id")
    {
        using var connection = await _sqlFactory.CreateConnectionAsync();
        await connection.OpenAsync();

        var sql = $"DELETE FROM [{tableName}] WHERE [{idColumn}] = @Id";
        var rowsAffected = await connection.ExecuteAsync(sql, new { Id = id });
        
        return rowsAffected > 0;
    }

    public async Task<List<Dictionary<string, object>>> QueryMongoAsync(string collectionName, string? filter = null, string? sort = null, int? limit = null)
    {
        var database = await _mongoFactory.GetDatabaseAsync();
        var collection = database.GetCollection<MongoDB.Bson.BsonDocument>(collectionName);

        var filterBuilder = MongoDB.Bson.BsonDocument.Parse(filter ?? "{}");
        var sortBuilder = !string.IsNullOrEmpty(sort) ? MongoDB.Bson.BsonDocument.Parse(sort) : null;

        var query = collection.Find(filterBuilder);
        
        if (sortBuilder != null)
        {
            query = query.Sort(sortBuilder);
        }
        
        if (limit.HasValue)
        {
            query = query.Limit(limit.Value);
        }

        var results = await query.ToListAsync();
        return results.Select(doc => BsonDocumentToDictionary(doc)).ToList();
    }

    public async Task<Dictionary<string, object>?> GetMongoByIdAsync(string collectionName, string id)
    {
        var database = await _mongoFactory.GetDatabaseAsync();
        var collection = database.GetCollection<MongoDB.Bson.BsonDocument>(collectionName);

        var filter = Builders<MongoDB.Bson.BsonDocument>.Filter.Eq("_id", id);
        var result = await collection.Find(filter).FirstOrDefaultAsync();
        
        return result != null ? BsonDocumentToDictionary(result) : null;
    }

    public async Task<string> InsertMongoAsync(string collectionName, Dictionary<string, object> data)
    {
        var database = await _mongoFactory.GetDatabaseAsync();
        var collection = database.GetCollection<MongoDB.Bson.BsonDocument>(collectionName);

        if (!data.ContainsKey("_id"))
        {
            data["_id"] = Guid.NewGuid().ToString();
        }

        var document = DictionaryToBsonDocument(data);
        await collection.InsertOneAsync(document);
        
        return data["_id"].ToString()!;
    }

    public async Task<bool> UpdateMongoAsync(string collectionName, string id, Dictionary<string, object> data)
    {
        var database = await _mongoFactory.GetDatabaseAsync();
        var collection = database.GetCollection<MongoDB.Bson.BsonDocument>(collectionName);

        var filter = Builders<MongoDB.Bson.BsonDocument>.Filter.Eq("_id", id);
        
        var updateBuilder = Builders<MongoDB.Bson.BsonDocument>.Update;
        var updates = new List<UpdateDefinition<MongoDB.Bson.BsonDocument>>();
        
        foreach (var kvp in data)
        {
            updates.Add(updateBuilder.Set(kvp.Key, ObjectToBsonValue(kvp.Value)));
        }
        
        var combinedUpdate = updateBuilder.Combine(updates);
        var result = await collection.UpdateOneAsync(filter, combinedUpdate);
        
        return result.ModifiedCount > 0;
    }

    public async Task<bool> DeleteMongoAsync(string collectionName, string id)
    {
        var database = await _mongoFactory.GetDatabaseAsync();
        var collection = database.GetCollection<MongoDB.Bson.BsonDocument>(collectionName);

        var filter = Builders<MongoDB.Bson.BsonDocument>.Filter.Eq("_id", id);
        var result = await collection.DeleteOneAsync(filter);
        
        return result.DeletedCount > 0;
    }

    public async Task<List<string>> GetTableNamesAsync()
    {
        using var connection = await _sqlFactory.CreateConnectionAsync();
        await connection.OpenAsync();

        var sql = @"
            SELECT TABLE_NAME 
            FROM INFORMATION_SCHEMA.TABLES 
            WHERE TABLE_TYPE = 'BASE TABLE' 
            ORDER BY TABLE_NAME";

        var results = await connection.QueryAsync<string>(sql);
        return results.ToList();
    }

    public async Task<List<Dictionary<string, object>>> GetTableSchemaAsync(string tableName)
    {
        using var connection = await _sqlFactory.CreateConnectionAsync();
        await connection.OpenAsync();

        var sql = @"
            SELECT 
                COLUMN_NAME,
                DATA_TYPE,
                IS_NULLABLE,
                COLUMN_DEFAULT,
                CHARACTER_MAXIMUM_LENGTH,
                NUMERIC_PRECISION,
                NUMERIC_SCALE
            FROM INFORMATION_SCHEMA.COLUMNS 
            WHERE TABLE_NAME = @TableName 
            ORDER BY ORDINAL_POSITION";

        var results = await connection.QueryAsync(sql, new { TableName = tableName });
        return results.Select(r => DapperRowToDictionary(r)).Cast<Dictionary<string, object>>().ToList();
    }

    public async Task<List<string>> GetCollectionNamesAsync()
    {
        var database = await _mongoFactory.GetDatabaseAsync();
        var collections = await database.ListCollectionNamesAsync();
        return await collections.ToListAsync();
    }

    private Dictionary<string, object> DapperRowToDictionary(dynamic dapperRow)
    {
        var result = new Dictionary<string, object>();
        
        if (dapperRow is IDictionary<string, object> dict)
        {
            foreach (var kvp in dict)
            {
                result[kvp.Key] = kvp.Value ?? DBNull.Value;
            }
        }
        else
        {
            var properties = ((object)dapperRow).GetType().GetProperties();
            foreach (var prop in properties)
            {
                result[prop.Name] = prop.GetValue(dapperRow) ?? DBNull.Value;
            }
        }
        
        return result;
    }

    private Dictionary<string, object> BsonDocumentToDictionary(MongoDB.Bson.BsonDocument document)
    {
        var result = new Dictionary<string, object>();
        
        foreach (var element in document)
        {
            result[element.Name] = BsonValueToObject(element.Value);
        }
        
        return result;
    }

    private object BsonValueToObject(MongoDB.Bson.BsonValue value)
    {
        return value.BsonType switch
        {
            MongoDB.Bson.BsonType.String => value.AsString,
            MongoDB.Bson.BsonType.Int32 => value.AsInt32,
            MongoDB.Bson.BsonType.Int64 => value.AsInt64,
            MongoDB.Bson.BsonType.Double => value.AsDouble,
            MongoDB.Bson.BsonType.Boolean => value.AsBoolean,
            MongoDB.Bson.BsonType.DateTime => value.AsDateTime,
            MongoDB.Bson.BsonType.ObjectId => value.AsObjectId.ToString(),
            MongoDB.Bson.BsonType.Array => value.AsBsonArray.Select(v => BsonValueToObject(v)).ToArray(),
            MongoDB.Bson.BsonType.Document => BsonDocumentToDictionary(value.AsBsonDocument),
            _ => value.ToString()
        };
    }

    private MongoDB.Bson.BsonDocument DictionaryToBsonDocument(Dictionary<string, object> data)
    {
        var document = new MongoDB.Bson.BsonDocument();
        
        foreach (var kvp in data)
        {
            document[kvp.Key] = ObjectToBsonValue(kvp.Value);
        }
        
        return document;
    }

    private MongoDB.Bson.BsonValue ObjectToBsonValue(object value)
    {
        return value switch
        {
            string s => new MongoDB.Bson.BsonString(s),
            int i => new MongoDB.Bson.BsonInt32(i),
            long l => new MongoDB.Bson.BsonInt64(l),
            double d => new MongoDB.Bson.BsonDouble(d),
            bool b => new MongoDB.Bson.BsonBoolean(b),
            DateTime dt => new MongoDB.Bson.BsonDateTime(dt),
            Dictionary<string, object> dict => DictionaryToBsonDocument(dict),
            _ => new MongoDB.Bson.BsonString(value.ToString())
        };
    }

    private Dictionary<string, object> ConvertJsonElements(Dictionary<string, object> data)
    {
        var converted = new Dictionary<string, object>();
        
        foreach (var kvp in data)
        {
            converted[kvp.Key] = ConvertJsonElement(kvp.Value);
        }
        
        return converted;
    }

    private object ConvertJsonElement(object value)
    {
        if (value is System.Text.Json.JsonElement jsonElement)
        {
            return jsonElement.ValueKind switch
            {
                System.Text.Json.JsonValueKind.String => jsonElement.GetString()!,
                System.Text.Json.JsonValueKind.Number => jsonElement.TryGetInt32(out var intValue) ? intValue : jsonElement.GetDecimal(),
                System.Text.Json.JsonValueKind.True => true,
                System.Text.Json.JsonValueKind.False => false,
                System.Text.Json.JsonValueKind.Null => DBNull.Value,
                System.Text.Json.JsonValueKind.Array => jsonElement.EnumerateArray().Select(x => ConvertJsonElement(x)).ToArray(),
                System.Text.Json.JsonValueKind.Object => ConvertJsonObject(jsonElement),
                _ => jsonElement.ToString()
            };
        }
        
        return value;
    }

    private Dictionary<string, object> ConvertJsonObject(System.Text.Json.JsonElement jsonElement)
    {
        var result = new Dictionary<string, object>();
        
        foreach (var property in jsonElement.EnumerateObject())
        {
            result[property.Name] = ConvertJsonElement(property.Value);
        }
        
        return result;
    }

    private static bool IsValidIdentifier(string identifier)
    {
        if (string.IsNullOrWhiteSpace(identifier))
            return false;
        
        return System.Text.RegularExpressions.Regex.IsMatch(identifier, @"^[a-zA-Z_][a-zA-Z0-9_@#$]*$|^\[[a-zA-Z_][a-zA-Z0-9_@#$]*\]$");
    }

    private static string SanitizeIdentifier(string identifier)
    {
        var clean = identifier.Trim('[', ']');
        return $"[{clean}]";
    }

    private async Task<bool> TableExistsAsync(System.Data.IDbConnection connection, string tableName)
    {
        var cleanTableName = tableName.Trim('[', ']');
        var sql = @"
            SELECT COUNT(*) 
            FROM INFORMATION_SCHEMA.TABLES 
            WHERE TABLE_NAME = @TableName AND TABLE_TYPE = 'BASE TABLE'";
        
        var count = await connection.ExecuteScalarAsync<int>(sql, new { TableName = cleanTableName });
        return count > 0;
    }
}
