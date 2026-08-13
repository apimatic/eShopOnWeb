using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
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

namespace Microsoft.eShopWeb.PublicApi.SmsNotifications.OrderEndpoints;

public class OrderNotificationsResponse : BaseResponse
{
    public int OrderId { get; set; }

    /// <summary>Each message sent for this order, and what became of it. Each carries its own notificationId.</summary>
    public List<NotificationDto> Notifications { get; set; } = new();
}

/// <summary>
/// Returns what was sent for one of the caller's orders and what became of each message. The order
/// lookup is scoped to the caller, so another shopper's order is not found. Delivery outcomes are
/// refreshed from the provider before being returned.
/// </summary>
public class OrderNotificationsEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/orders/{orderId:int}/notifications",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (
                int orderId,
                IReadRepository<Order> orderRepository,
                IRepository<OrderNotification> notificationRepository,
                IOrderNotificationService notificationService,
                HttpContext httpContext,
                CancellationToken cancellationToken) =>
            {
                var buyerId = CallerIdentity.GetBuyerId(httpContext);
                if (string.IsNullOrEmpty(buyerId))
                    return Results.Unauthorized();

                var order = await orderRepository.GetByIdAsync(orderId, cancellationToken);
                // Not found or not the caller's: both look the same, so ownership is never revealed.
                if (order is null || order.BuyerId != buyerId)
                    return Results.NotFound();

                var notifications = await notificationRepository.ListAsync(
                    new OrderNotificationsByOrderSpecification(orderId), cancellationToken);

                foreach (var notification in notifications)
                    await notificationService.RefreshDeliveryStateAsync(notification, cancellationToken);

                var response = new OrderNotificationsResponse
                {
                    OrderId = orderId,
                    Notifications = notifications.Select(NotificationDto.FromEntity).ToList()
                };
                return Results.Ok(response);
            })
            .Produces<OrderNotificationsResponse>()
            .WithTags("OrderEndpoints");
    }
}
