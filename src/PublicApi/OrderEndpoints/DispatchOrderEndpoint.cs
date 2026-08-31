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

public class DispatchOrderResponse : BaseResponse
{
    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
    public List<int> NotificationIds { get; set; } = new();
    public List<int> ScheduledFollowUpIds { get; set; } = new();
}

/// <summary>
/// Marks an order dispatched (operator). The shopper is told it is on its way and a delivery
/// follow-up message is queued with the provider for a few days later.
/// </summary>
public class DispatchOrderEndpoint : IEndpoint<IResult, int, CancellationToken>
{
    private readonly IRepository<Order> _orders;
    private readonly NotificationService _notifications;

    public DispatchOrderEndpoint(IRepository<Order> orders, NotificationService notifications)
    {
        _orders = orders;
        _notifications = notifications;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/dispatch",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, CancellationToken ct) =>
            {
                return await HandleAsync(orderId, ct);
            })
            .Produces<DispatchOrderResponse>()
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
            order.MarkDispatched();
        }
        catch (System.InvalidOperationException ex)
        {
            return Results.Conflict(new { message = ex.Message });
        }
        await _orders.UpdateAsync(order, ct);

        var notifications = await _notifications.NotifyAsync(order, NotificationKind.OrderDispatched,
            $"eShop: good news — your order #{order.Id} is on its way!", ct);

        var followUps = await _notifications.ScheduleFollowUpAsync(order,
            $"eShop: your order #{order.Id} should have arrived by now — how did the delivery go? We'd love to hear from you.", ct);

        return Results.Ok(new DispatchOrderResponse
        {
            OrderId = order.Id,
            Status = order.Status.ToString(),
            NotificationIds = notifications.Select(n => n.Id).ToList(),
            ScheduledFollowUpIds = followUps.Select(n => n.Id).ToList()
        });
    }
}
