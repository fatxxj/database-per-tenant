using Dapper;
using Microsoft.Extensions.Logging;
using TenantDbService.Api.Data.Sql;

namespace TenantDbService.Api.Features.Orders;

public class Order
{
    public string Id { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class OrdersRepository
{
    private readonly SqlConnectionFactory _sqlFactory;
    private readonly ILogger<OrdersRepository> _logger;

    public OrdersRepository(SqlConnectionFactory sqlFactory, ILogger<OrdersRepository> logger)
    {
        _sqlFactory = sqlFactory;
        _logger = logger;
    }

    public async Task<List<Order>> GetOrdersAsync()
    {
        using var connection = await _sqlFactory.CreateConnectionAsync();
        await connection.OpenAsync();

        const string sql = @"
            SELECT Id, Code, Amount, CreatedAt 
            FROM Orders 
            ORDER BY CreatedAt DESC 
            OFFSET 0 ROWS FETCH NEXT 50 ROWS ONLY";

        var orders = await connection.QueryAsync<Order>(sql);
        return orders.ToList();
    }

    public async Task<Order?> GetOrderByIdAsync(string id)
    {
        using var connection = await _sqlFactory.CreateConnectionAsync();
        await connection.OpenAsync();

        const string sql = @"
            SELECT Id, Code, Amount, CreatedAt 
            FROM Orders 
            WHERE Id = @Id";

        var order = await connection.QueryFirstOrDefaultAsync<Order>(sql, new { Id = id });
        return order;
    }

    public async Task<Order> CreateOrderAsync(string code, decimal amount)
    {
        using var connection = await _sqlFactory.CreateConnectionAsync();
        await connection.OpenAsync();

        var order = new Order
        {
            Id = Guid.NewGuid().ToString(),
            Code = code,
            Amount = amount,
            CreatedAt = DateTime.UtcNow
        };

        const string sql = @"
            INSERT INTO Orders (Id, Code, Amount, CreatedAt) 
            VALUES (@Id, @Code, @Amount, @CreatedAt)";

        await connection.ExecuteAsync(sql, order);
        
        _logger.LogInformation("Created order: {OrderId} with code: {OrderCode}", order.Id, order.Code);
        
        return order;
    }

    public async Task<bool> DeleteOrderAsync(string id)
    {
        using var connection = await _sqlFactory.CreateConnectionAsync();
        await connection.OpenAsync();

        const string sql = "DELETE FROM Orders WHERE Id = @Id";
        var rowsAffected = await connection.ExecuteAsync(sql, new { Id = id });
        
        return rowsAffected > 0;
    }

    public async Task<List<Order>> GetOrdersByCodeAsync(string code)
    {
        using var connection = await _sqlFactory.CreateConnectionAsync();
        await connection.OpenAsync();

        const string sql = @"
            SELECT Id, Code, Amount, CreatedAt 
            FROM Orders 
            WHERE Code = @Code 
            ORDER BY CreatedAt DESC";

        var orders = await connection.QueryAsync<Order>(sql, new { Code = code });
        return orders.ToList();
    }
}
