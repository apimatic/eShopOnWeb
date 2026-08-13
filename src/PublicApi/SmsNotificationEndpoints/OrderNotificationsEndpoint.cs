using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SmsNotificationEndpoints;

/// <summary>
/// GET /api/orders/{orderId}/notifications — what was sent for this order and what became of each
/// message. Shopper-scoped: only the caller's own order. Each entry carries its own notificationId.
/// </summary>
public class OrderNotificationsEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/orders/{orderId:int}/notifications",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (
                int orderId,
                ClaimsPrincipal user,
                IOrderNotificationService service,
                CancellationToken cancellationToken) =>
            {
                var buyerId = user.GetBuyerId();
                if (string.IsNullOrEmpty(buyerId)) return Results.Unauthorized();

                // buyerScope ensures a shopper cannot see another shopper's order notifications.
                var notifications = await service.GetOrderNotificationsAsync(orderId, buyerId, cancellationToken);
                if (notifications == null) return Results.NotFound();

                var response = new OrderNotificationsResponse(orderId, notifications.Select(NotificationDto.From).ToList());
                return Results.Ok(response);
            })
            .Produces<OrderNotificationsResponse>()
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("OrderNotificationEndpoints");
    }
}
