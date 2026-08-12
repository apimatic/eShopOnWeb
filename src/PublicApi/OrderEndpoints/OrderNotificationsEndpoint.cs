using System.Collections.Generic;
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

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class OrderNotificationsResponse : BaseResponse
{
    public int OrderId { get; set; }
    public List<NotificationDto> Notifications { get; set; } = new();
}

/// <summary>
/// What was sent for this order and what became of each message. Each entry carries its own
/// notificationId — the identifier the operator endpoints act on. Scoped to the order's owner.
/// </summary>
public class OrderNotificationsEndpoint : IEndpoint
{
    private readonly IOrderNotificationService _orderNotificationService;

    public OrderNotificationsEndpoint(IOrderNotificationService orderNotificationService)
    {
        _orderNotificationService = orderNotificationService;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/orders/{orderId}/notifications",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, ClaimsPrincipal user, CancellationToken ct) => await HandleAsync(orderId, user, ct))
            .Produces<OrderNotificationsResponse>()
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(int orderId, ClaimsPrincipal user, CancellationToken ct)
    {
        var buyerId = user.GetUsername();
        if (string.IsNullOrEmpty(buyerId))
            return Results.Unauthorized();

        var notifications = await _orderNotificationService.GetOwnedOrderNotificationsAsync(buyerId, orderId, ct);
        if (notifications is null)
            return Results.NotFound();

        var response = new OrderNotificationsResponse
        {
            OrderId = orderId,
            Notifications = notifications.Select(NotificationDto.From).ToList()
        };
        return Results.Ok(response);
    }
}
