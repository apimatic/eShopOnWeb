using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.PublicApi.Configuration;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// What was sent for this order and what became of each message. Each entry carries its own
/// <c>notificationId</c> — the identifier the operator endpoints act on. Scoped to the caller's own order.
/// </summary>
public class OrderNotificationsEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/orders/{orderId:int}/notifications",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, HttpContext http, IOrderNotificationService service) =>
                await HandleAsync(orderId, http, service, http.RequestAborted))
            .Produces<OrderNotificationsResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(int orderId, HttpContext http, IOrderNotificationService service, CancellationToken ct)
    {
        var buyerId = http.User.GetBuyerId();
        if (string.IsNullOrEmpty(buyerId))
            return Results.Unauthorized();

        var notifications = await service.GetOwnedOrderNotificationsAsync(buyerId, orderId, ct);
        if (notifications is null)
            return Results.NotFound();

        return Results.Ok(new OrderNotificationsResponse
        {
            OrderId = orderId,
            Notifications = notifications.Select(n => n.ToView()).ToList()
        });
    }
}
