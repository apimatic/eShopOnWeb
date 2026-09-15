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
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// GET /api/orders/{orderId}/notifications — what was sent for this order, and what became of each message.
/// Each entry carries its own notificationId (what the operator endpoints act on). Shopper-scoped to the
/// caller's own order.
/// </summary>
public class OrderNotificationsEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/orders/{orderId:int}/notifications",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (
                int orderId,
                IReadRepository<Order> orderRepository,
                IOrderNotificationService notifications,
                ClaimsPrincipal user,
                CancellationToken ct) =>
            {
                var buyerId = user.Identity?.Name;
                if (string.IsNullOrEmpty(buyerId))
                {
                    return Results.Unauthorized();
                }

                // Scope to the caller's own order: another shopper's order is Not Found to this caller.
                var order = await orderRepository.GetByIdAsync(orderId, ct);
                if (order is null || order.BuyerId != buyerId)
                {
                    return Results.NotFound();
                }

                var list = await notifications.GetOrderNotificationsAsync(orderId, ct);
                var response = new OrderNotificationsResponse
                {
                    OrderId = orderId,
                    Notifications = list.Select(NotificationDto.From).ToList()
                };

                return Results.Ok(response);
            })
            .Produces<OrderNotificationsResponse>()
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("OrderEndpoints");
    }
}
