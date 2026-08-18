using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// What was sent for an order, and what became of each message. Each entry carries its own
/// notificationId — the identifier the operator endpoints act on. Scoped to the order's owner
/// (an administrator may also read it, to discover a message to resend).
/// </summary>
public class OrderNotificationsEndpoint : IEndpoint<IResult, OrderNotificationsQuery, IOrderNotificationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/orders/{orderId}/notifications",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, ClaimsPrincipal user, IOrderNotificationService service) =>
            {
                var callerId = user.UserName();
                if (string.IsNullOrEmpty(callerId)) return Results.Unauthorized();
                return await HandleAsync(new OrderNotificationsQuery(orderId, callerId, user.IsAdministrator()), service);
            })
            .Produces<OrderNotificationsResponse>()
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(OrderNotificationsQuery request, IOrderNotificationService service)
    {
        var order = await service.GetOrderAsync(request.OrderId);
        if (order is null) return Results.NotFound();

        // One shopper must never see another's order. Return an explicit 403 (Results.Forbid()
        // would resolve to the cookie scheme's redirect because Identity owns the default scheme).
        if (order.BuyerId != request.CallerId && !request.IsAdministrator)
        {
            return Results.StatusCode(StatusCodes.Status403Forbidden);
        }

        var notifications = await service.GetNotificationsForOrderAsync(request.OrderId);
        var response = new OrderNotificationsResponse
        {
            OrderId = request.OrderId,
            Notifications = notifications.Select(NotificationDto.From).ToList()
        };
        return Results.Ok(response);
    }
}
