using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.Infrastructure.SquareIntegration;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// The caller's order with its Square order id and the gift message as Square holds it now (so an edit made by
/// staff in Square shows up here). Another shopper's order is reported as not found.
/// </summary>
public class GetMyOrderEndpoint : IEndpoint<IResult, int, ClaimsPrincipal, SquareOrderService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-orders/{orderId:int}",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, ClaimsPrincipal user, SquareOrderService orders, HttpContext context) =>
            {
                return await HandleAsync(orderId, user, orders, context.RequestAborted);
            })
            .Produces<MyOrderResponse>()
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("OrderEndpoints");
    }

    public Task<IResult> HandleAsync(int orderId, ClaimsPrincipal user, SquareOrderService orders) =>
        HandleAsync(orderId, user, orders, CancellationToken.None);

    public async Task<IResult> HandleAsync(int orderId, ClaimsPrincipal user, SquareOrderService orders, CancellationToken requestAborted)
    {
        var buyerId = user.Identity?.Name;
        if (string.IsNullOrEmpty(buyerId)) return Results.Unauthorized();

        using var deadline = SquareTimeouts.Deadline(requestAborted, SquareTimeouts.Request);
        var found = await orders.GetMyOrderAsync(buyerId, orderId, deadline.Token);
        if (found is null) return Results.NotFound();

        var order = found.Order;
        return Results.Ok(new MyOrderResponse
        {
            OrderId = order.Id,
            OrderDate = order.OrderDate,
            Total = order.Total(),
            Items = order.OrderItems.Select(i => new MyOrderItemDto
            {
                CatalogItemId = i.ItemOrdered.CatalogItemId,
                ProductName = i.ItemOrdered.ProductName,
                UnitPrice = i.UnitPrice,
                Units = i.Units,
            }).ToList(),
            SquareOrderId = found.SquareOrderId,
            SquareStatus = found.SquareStatus,
            SquareOrderState = found.SquareOrderState,
            GiftMessage = found.GiftMessage,
            GiftMessageAvailable = found.GiftMessageAvailable,
            Warning = found.Warning,
        });
    }
}

public class MyOrderResponse : BaseResponse
{
    public int OrderId { get; set; }
    public DateTimeOffset OrderDate { get; set; }
    public decimal Total { get; set; }
    public List<MyOrderItemDto> Items { get; set; } = new();
    public string? SquareOrderId { get; set; }
    public string SquareStatus { get; set; } = string.Empty;
    public string? SquareOrderState { get; set; }

    /// <summary>Read from Square on every request; null when the order has no gift message.</summary>
    public string? GiftMessage { get; set; }

    /// <summary>False when Square could not be read, so a null <see cref="GiftMessage"/> means "unknown", not "none".</summary>
    public bool GiftMessageAvailable { get; set; }
    public string? Warning { get; set; }
}

public class MyOrderItemDto
{
    public int CatalogItemId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public decimal UnitPrice { get; set; }
    public int Units { get; set; }
}
