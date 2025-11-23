using Microsoft.AspNetCore.Mvc;
using TenantDbService.Api.Common;
using TenantDbService.Api.Data.Sql;

namespace TenantDbService.Api.Endpoints;

public static class DynamicDataEndpoints
{
    public static void MapDynamicDataEndpoints(this WebApplication app)
    {
        app.MapPost("/api/data/tables/create", async (DynamicSchemaService schemaService, SqlConnectionFactory sqlFactory, [FromBody] TableDefinition tableDefinition) =>
        {
            try
            {
                if (string.IsNullOrWhiteSpace(tableDefinition.Name))
                    return Results.BadRequest(new { error = "Table name is required" });

                if (tableDefinition.Columns == null || !tableDefinition.Columns.Any())
                    return Results.BadRequest(new { error = "Table must have at least one column" });

                if (!tableDefinition.Columns.Any(c => c.IsPrimaryKey))
                    return Results.BadRequest(new { error = "Table must have at least one primary key column" });

                var tempSchema = new SchemaDefinition
                {
                    Version = Constants.SchemaDefaults.DefaultVersion,
                    Name = "Temp Schema",
                    Tables = new List<TableDefinition> { tableDefinition }
                };
                
                var validation = schemaService.ValidateSchema(tempSchema);
                if (!validation.IsValid)
                    return Results.BadRequest(new { error = Constants.ErrorMessages.InvalidSchemaDefinition, errors = validation.Errors });

                using var connection = await sqlFactory.CreateConnectionAsync();
                await connection.OpenAsync();
                await schemaService.CreateTableAsync(connection, tableDefinition);
                
                return Results.Ok(new { message = $"Table '{tableDefinition.Name}' created successfully" });
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        })
        .RequireAuthorization()
        .WithTags("SQL Server - Dynamic Tables")
        .WithName("CreateTable");

        app.MapGet("/api/data/tables", async (DynamicDataService dataService) =>
        {
            var tables = await dataService.GetTableNamesAsync();
            return Results.Ok(tables);
        })
        .RequireAuthorization()
        .WithTags("SQL Server - Dynamic Tables")
        .WithName("GetTableNames");

        app.MapGet("/api/data/tables/{tableName}/schema", async (DynamicDataService dataService, string tableName) =>
        {
            var schema = await dataService.GetTableSchemaAsync(tableName);
            return Results.Ok(schema);
        })
        .RequireAuthorization()
        .WithTags("SQL Server - Dynamic Tables")
        .WithName("GetTableSchema");

        app.MapGet("/api/data/tables/{tableName}", async (DynamicDataService dataService, string tableName, string? where, string? orderBy, int? limit) =>
        {
            var data = await dataService.QueryAsync(tableName, where, orderBy, limit);
            return Results.Ok(data);
        })
        .RequireAuthorization()
        .WithTags("SQL Server - Dynamic Tables")
        .WithName("QueryTable");

        app.MapGet("/api/data/tables/{tableName}/{id}", async (DynamicDataService dataService, string tableName, string id) =>
        {
            var data = await dataService.GetByIdAsync(tableName, id);
            if (data == null)
                return Results.NotFound();
            
            return Results.Ok(data);
        })
        .RequireAuthorization()
        .WithTags("SQL Server - Dynamic Tables")
        .WithName("GetTableRecord");

        app.MapPost("/api/data/tables/{tableName}", async (DynamicDataService dataService, string tableName, [FromBody] Dictionary<string, object> data, ILoggerFactory loggerFactory) =>
        {
            var logger = loggerFactory.CreateLogger("DynamicDataEndpoints");
            logger.LogInformation("POST /api/data/tables/{TableName} - Received tableName: '{TableName}'", tableName, tableName);
            
            var id = await dataService.InsertAsync(tableName, data);
            return Results.Created($"/api/data/tables/{tableName}/{id}", new { id });
        })
        .RequireAuthorization()
        .WithTags("SQL Server - Dynamic Tables")
        .WithName("InsertTableRecord");

        app.MapPut("/api/data/tables/{tableName}/{id}", async (DynamicDataService dataService, string tableName, string id, [FromBody] Dictionary<string, object> data) =>
        {
            var success = await dataService.UpdateAsync(tableName, id, data);
            if (!success)
                return Results.NotFound();
            
            return Results.Ok(new { message = "Updated successfully" });
        })
        .RequireAuthorization()
        .WithTags("SQL Server - Dynamic Tables")
        .WithName("UpdateTableRecord");

        app.MapDelete("/api/data/tables/{tableName}/{id}", async (DynamicDataService dataService, string tableName, string id) =>
        {
            var success = await dataService.DeleteAsync(tableName, id);
            if (!success)
                return Results.NotFound();
            
            return Results.Ok(new { message = "Deleted successfully" });
        })
        .RequireAuthorization()
        .WithTags("SQL Server - Dynamic Tables")
        .WithName("DeleteTableRecord");
    }
}

