using Microsoft.AspNetCore.Mvc;
using TenantDbService.Api.Features.Orders;

namespace TenantDbService.Api.Endpoints;

public static class OrderEndpoints
{
    public static void MapOrderEndpoints(this WebApplication app)
    {
        app.MapGet("/api/orders", async (OrdersRepository ordersRepository, HttpContext context) =>
        {
            var orders = await ordersRepository.GetOrdersAsync();
            return Results.Ok(orders);
        })
        .RequireAuthorization()
        .WithTags("SQL Server - Orders")
        .WithName("GetOrders");

        app.MapPost("/api/orders", async (OrdersRepository ordersRepository, [FromBody] CreateOrderRequest request) =>
        {
            if (string.IsNullOrEmpty(request.Code))
                return Results.BadRequest(new { error = "Code is required" });
            
            var order = await ordersRepository.CreateOrderAsync(request.Code, request.Amount);
            return Results.Created($"/api/orders/{order.Id}", order);
        })
        .RequireAuthorization()
        .WithTags("SQL Server - Orders")
        .WithName("CreateOrder");

        app.MapGet("/api/orders/{id}", async (OrdersRepository ordersRepository, string id) =>
        {
            var order = await ordersRepository.GetOrderByIdAsync(id);
            if (order == null)
                return Results.NotFound();
            
            return Results.Ok(order);
        })
        .RequireAuthorization()
        .WithTags("SQL Server - Orders")
        .WithName("GetOrderById");
    }
}

public record CreateOrderRequest(string Code, decimal Amount);


