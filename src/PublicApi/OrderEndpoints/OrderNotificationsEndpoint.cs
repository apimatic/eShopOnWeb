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
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.PublicApi.NotificationEndpoints;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// What was sent for one of the signed-in shopper's orders and what became of each message. Each entry carries
/// its own <c>notificationId</c>, which is what the operator endpoints act on. Scoped to the caller's own order.
/// </summary>
public class OrderNotificationsEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/orders/{orderId:int}/notifications",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (
                int orderId,
                ClaimsPrincipal user,
                IReadRepository<Order> orderRepository,
                IOrderNotificationService notifications,
                CancellationToken ct) =>
            {
                var buyerId = user.GetBuyerId();
                if (string.IsNullOrEmpty(buyerId))
                    return Results.Unauthorized();

                // Not found (rather than forbidden) when it isn't the caller's order, so existence isn't leaked.
                var order = await orderRepository.GetByIdAsync(orderId, ct);
                if (order is null || order.BuyerId != buyerId)
                    return Results.NotFound();

                var orderNotifications = await notifications.GetOrderNotificationsAsync(orderId, ct);
                var response = new OrderNotificationsResponse(
                    orderId,
                    orderNotifications.Select(NotificationDto.From).ToList());

                return Results.Ok(response);
            })
            .Produces<OrderNotificationsResponse>()
            .WithTags("OrderEndpoints");
    }
}

public record OrderNotificationsResponse(int OrderId, IReadOnlyList<NotificationDto> Notifications);
