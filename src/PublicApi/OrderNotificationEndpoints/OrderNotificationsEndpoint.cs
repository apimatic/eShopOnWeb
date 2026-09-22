using System.Collections.Generic;
using System.Linq;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderNotificationEndpoints;

public class OrderNotificationsResponse
{
    public int OrderId { get; set; }
    public List<NotificationStatusDto> Notifications { get; set; } = new();
}

/// <summary>
/// What was sent for an order and what became of each message. Each entry carries its own
/// notificationId (what the operator endpoints act on). A shopper sees only their own order;
/// an operator may view any.
/// </summary>
public class OrderNotificationsEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/orders/{orderId:int}/notifications",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, HttpContext http, IReadRepository<Order> orderRepository,
             IReadRepository<OrderNotification> notificationRepository, System.Threading.CancellationToken ct) =>
            {
                var callerId = CallerContext.GetUserName(http);
                if (string.IsNullOrEmpty(callerId)) return Results.Unauthorized();

                var order = await orderRepository.GetByIdAsync(orderId, ct);
                if (order is null) return Results.NotFound();

                // One shopper must never see another's order; operators may view any.
                if (order.BuyerId != callerId && !CallerContext.IsAdministrator(http))
                    return Results.NotFound();

                var notifications = await notificationRepository.ListAsync(
                    new OrderNotificationsByOrderSpecification(orderId), ct);

                return Results.Ok(new OrderNotificationsResponse
                {
                    OrderId = orderId,
                    Notifications = notifications.Select(NotificationStatusDto.From).ToList()
                });
            })
            .Produces<OrderNotificationsResponse>()
            .WithTags("OrderNotificationEndpoints");
    }
}
