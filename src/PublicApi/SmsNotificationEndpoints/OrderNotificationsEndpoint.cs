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
/// What was sent for one of the caller's orders, and what became of each message. Each entry carries its own
/// notificationId (what the operator endpoints act on). Delivery outcomes are refreshed from the provider.
/// An order that does not exist or is owned by someone else is a 404.
/// </summary>
public class OrderNotificationsEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/orders/{orderId}/notifications",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, ClaimsPrincipal user, IOrderMessagingService service, CancellationToken ct) =>
            {
                var buyerId = user.GetBuyerId();
                if (string.IsNullOrEmpty(buyerId))
                    return Results.Unauthorized();

                var notifications = await service.GetOrderNotificationsForBuyerAsync(orderId, buyerId, ct);
                if (notifications is null)
                    return Results.NotFound();

                return Results.Ok(notifications.Select(n => n.ToDto()).ToList());
            })
            .Produces<System.Collections.Generic.List<NotificationDto>>()
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("OrderNotificationEndpoints");
    }
}
