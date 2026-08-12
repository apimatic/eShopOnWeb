using System.Linq;
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
/// What was sent for an order and what became of each message. Each entry carries its own
/// <c>notificationId</c> — the identifier the operator endpoints act on. Scoped to the order's owner,
/// or an operator (administrator).
/// </summary>
public class OrderNotificationsEndpoint : IEndpoint<IResult, int, HttpContext, IOrderNotificationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/orders/{orderId}/notifications",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, HttpContext http, IOrderNotificationService service) =>
                await HandleAsync(orderId, http, service))
            .Produces<NotificationDto[]>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(int orderId, HttpContext http, IOrderNotificationService service)
    {
        var buyerId = http.User.GetBuyerId();
        if (string.IsNullOrEmpty(buyerId))
        {
            return Results.Unauthorized();
        }

        var order = await service.GetOrderAsync(orderId);
        // A shopper can only see their own order's notifications; return 404 rather than leak existence.
        if (order is null || (!http.User.IsAdministrator() && order.BuyerId != buyerId))
        {
            return Results.NotFound();
        }

        var notifications = await service.GetOrderNotificationsAsync(orderId);
        return Results.Ok(notifications.Select(NotificationDto.From).ToList());
    }
}
