using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.PublicApi.Notifications;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class CancelOrderResponse : BaseResponse
{
    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
    public List<int> NotificationIds { get; set; } = new();
}

/// <summary>
/// Cancels an order (operator). The shopper is told, and any follow-up message still queued
/// with the provider is cancelled so it never reaches them.
/// </summary>
public class CancelOrderEndpoint : IEndpoint<IResult, int, CancellationToken>
{
    private readonly IRepository<Order> _orders;
    private readonly NotificationService _notifications;

    public CancelOrderEndpoint(IRepository<Order> orders, NotificationService notifications)
    {
        _orders = orders;
        _notifications = notifications;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/cancel",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, CancellationToken ct) =>
            {
                return await HandleAsync(orderId, ct);
            })
            .Produces<CancelOrderResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(int orderId, CancellationToken ct)
    {
        var order = await _orders.GetByIdAsync(orderId, ct);
        if (order is null)
        {
            return Results.NotFound();
        }

        try
        {
            order.MarkCancelled();
        }
        catch (System.InvalidOperationException ex)
        {
            return Results.Conflict(new { message = ex.Message });
        }
        await _orders.UpdateAsync(order, ct);

        // Call off any provider-queued follow-up first, then tell the shopper.
        await _notifications.CancelPendingFollowUpsAsync(order, ct);

        var notifications = await _notifications.NotifyAsync(order, NotificationKind.OrderCancelled,
            $"eShop: your order #{order.Id} has been cancelled. If this is unexpected, please contact support.", ct);

        return Results.Ok(new CancelOrderResponse
        {
            OrderId = order.Id,
            Status = order.Status.ToString(),
            NotificationIds = notifications.Select(n => n.Id).ToList()
        });
    }
}
