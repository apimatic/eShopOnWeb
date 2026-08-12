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
using Microsoft.eShopWeb.PublicApi.Configuration;
using Microsoft.eShopWeb.PublicApi.NotificationEndpoints;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// What was sent for one order and what became of each message. Shopper-scoped: a shopper sees only
/// their own order's notifications; an operator (administrator) may see any order's. Each entry
/// carries its own notificationId, which is what the operator endpoints act on.
/// </summary>
public class GetOrderNotificationsEndpoint : IEndpoint<IResult, int, ClaimsPrincipal, IOrderNotificationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/orders/{orderId}/notifications",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, ClaimsPrincipal user, IOrderNotificationService service) =>
            {
                return await HandleAsync(orderId, user, service);
            })
            .Produces<OrderNotificationsResponse>()
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(int orderId, ClaimsPrincipal user, IOrderNotificationService service)
    {
        var buyerId = user.GetBuyerId();
        if (string.IsNullOrEmpty(buyerId))
        {
            return Results.Unauthorized();
        }

        // Operators may view any order; a shopper is restricted to their own.
        var restrictToBuyerId = user.IsAdministrator() ? null : buyerId;

        var notifications = await service.GetOrderNotificationsAsync(orderId, restrictToBuyerId);
        if (notifications is null)
        {
            return Results.NotFound();
        }

        var response = new OrderNotificationsResponse
        {
            OrderId = orderId,
            Notifications = notifications.Select(NotificationDto.FromEntity).ToList()
        };
        return Results.Ok(response);
    }
}

public class OrderNotificationsResponse
{
    public int OrderId { get; init; }
    public List<NotificationDto> Notifications { get; init; } = new();
}
