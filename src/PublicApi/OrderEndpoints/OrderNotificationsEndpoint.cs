using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.PublicApi.SmsNotifications;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// What was sent for one order, and what became of each message. Each entry carries its own
/// notificationId — the identifier the operator endpoints act on. Readable by the order's own
/// shopper or by an operator (administrator); one shopper never sees another's order.
/// </summary>
public class OrderNotificationsEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/orders/{orderId}/notifications",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (
                int orderId,
                ClaimsPrincipal user,
                IRepository<Order> orderRepository,
                IOrderNotificationService notifications) =>
            {
                var ownerId = user.GetOwnerId();
                if (string.IsNullOrEmpty(ownerId))
                {
                    return Results.Unauthorized();
                }

                var order = await orderRepository.GetByIdAsync(orderId);
                if (order is null)
                {
                    return Results.NotFound();
                }

                // The order's own shopper, or an operator, may read this; anyone else is told
                // nothing (404 rather than 403, so another shopper's order is not even revealed).
                if (order.BuyerId != ownerId && !user.IsAdministrator())
                {
                    return Results.NotFound();
                }

                var orderNotifications = await notifications.GetNotificationsForOrderAsync(orderId);
                return Results.Ok(new OrderNotificationsResponse
                {
                    OrderId = orderId,
                    Notifications = orderNotifications.Select(n => n.ToView()).ToList()
                });
            })
            .Produces<OrderNotificationsResponse>()
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("OrderEndpoints");
    }
}

public class OrderNotificationsResponse
{
    public int OrderId { get; set; }
    public List<NotificationView> Notifications { get; set; } = new();
}
