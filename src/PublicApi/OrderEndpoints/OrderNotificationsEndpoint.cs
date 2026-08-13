using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.PublicApi.SmsNotifications;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// GET /api/orders/{orderId}/notifications — what was sent for this order, and what became of each
/// message. Each entry carries its own <c>notificationId</c>, which the operator endpoints act on.
/// A shopper sees only their own order's notifications; an operator may view any order's.
/// </summary>
public class OrderNotificationsEndpoint : IEndpoint<IResult, int, IOrderNotificationService, ClaimsPrincipal>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/orders/{orderId:int}/notifications",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, IOrderNotificationService service, ClaimsPrincipal user) =>
                await HandleAsync(orderId, service, user))
            .Produces<IEnumerable<NotificationDto>>()
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(int orderId, IOrderNotificationService service, ClaimsPrincipal user)
    {
        var buyerId = user.GetBuyerId();
        if (string.IsNullOrEmpty(buyerId))
        {
            return Results.Unauthorized();
        }

        var view = await service.GetOrderNotificationsAsync(orderId);
        if (view is null)
        {
            return Results.NotFound();
        }

        // Shopper-scoped: a non-operator may only see their own order. Return 404 rather than 403 so
        // the existence of another shopper's order is not revealed.
        if (!user.IsAdministrator() && view.BuyerId != buyerId)
        {
            return Results.NotFound();
        }

        return Results.Ok(view.Notifications.Select(n => n.ToDto()).ToList());
    }
}
