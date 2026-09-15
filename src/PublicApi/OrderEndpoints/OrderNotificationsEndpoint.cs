using System.Collections.Generic;
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
/// What was sent for one of the caller's own orders, and what became of each message. Every entry
/// carries its own notificationId — the identifier the operator endpoints act on.
/// </summary>
public class OrderNotificationsEndpoint : IEndpoint<IResult, int, IOrderNotificationService, ClaimsPrincipal>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/orders/{orderId}/notifications",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, IOrderNotificationService service, ClaimsPrincipal user) =>
            {
                return await HandleAsync(orderId, service, user);
            })
            .Produces<OrderNotificationsResponse>()
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(int orderId, IOrderNotificationService service, ClaimsPrincipal user)
    {
        var ownerId = user.GetUserName();
        if (string.IsNullOrEmpty(ownerId))
            return Results.Unauthorized();

        var notifications = await service.GetOrderNotificationsAsync(ownerId, orderId);
        if (notifications is null)
            return Results.NotFound();

        return Results.Ok(new OrderNotificationsResponse { OrderId = orderId, Notifications = notifications });
    }
}

public class OrderNotificationsResponse : BaseResponse
{
    public int OrderId { get; set; }
    public IReadOnlyList<NotificationView> Notifications { get; set; } = new List<NotificationView>();
}
