using System.Collections.Generic;
using System.Threading;
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
/// What was sent for one of the caller's own orders, and what became of each message. Each entry
/// carries its own notificationId — the identifier the operator endpoints act on.
/// </summary>
public class OrderNotificationsEndpoint : IEndpoint<IResult, IOrderNotificationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/orders/{orderId:int}/notifications",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, HttpContext http, IOrderNotificationService service, CancellationToken ct) =>
            {
                var caller = http.User.Identity?.Name;
                if (string.IsNullOrEmpty(caller))
                {
                    return Results.Unauthorized();
                }

                var notifications = await service.GetOrderNotificationsAsync(orderId, caller, ct);
                if (notifications is null)
                {
                    // Either no such order, or it is not the caller's.
                    return Results.NotFound();
                }

                return Results.Ok(new OrderNotificationsResponse { OrderId = orderId, Notifications = notifications });
            })
            .Produces<OrderNotificationsResponse>()
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("OrderEndpoints");
    }

    public Task<IResult> HandleAsync(IOrderNotificationService service) => Task.FromResult<IResult>(Results.Empty);
}

public class OrderNotificationsResponse : BaseResponse
{
    public int OrderId { get; set; }
    public IReadOnlyList<NotificationView> Notifications { get; set; } = new List<NotificationView>();
}
