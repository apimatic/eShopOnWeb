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
using Microsoft.eShopWeb.PublicApi.Extensions;
using Microsoft.eShopWeb.PublicApi.NotificationEndpoints;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// GET /api/orders/{orderId}/notifications — what was sent for this order and what became of each message.
/// Shopper-scoped: acts only on the caller's own order. Each entry carries its own notificationId, which is
/// what the operator endpoints act on.
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
                IOrderNotificationService service,
                CancellationToken cancellationToken) =>
            {
                var buyerId = user.GetUserName();
                if (string.IsNullOrEmpty(buyerId))
                {
                    return Results.Unauthorized();
                }

                // Scope to the caller's own order; a shopper never sees another's.
                var order = await orderRepository.GetByIdAsync(orderId, cancellationToken);
                if (order is null || order.BuyerId != buyerId)
                {
                    return Results.NotFound();
                }

                var notifications = await service.GetNotificationsForOrderAsync(orderId, refreshFromProvider: true, cancellationToken);
                return Results.Ok(new OrderNotificationsResponse
                {
                    OrderId = orderId,
                    Notifications = notifications?.Select(NotificationDto.From).ToList() ?? new List<NotificationDto>()
                });
            })
            .Produces<OrderNotificationsResponse>()
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("OrderEndpoints");
    }
}
