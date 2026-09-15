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

namespace Microsoft.eShopWeb.PublicApi.OrderNotificationEndpoints;

/// <summary>
/// What was sent for one of the caller's own orders, and what became of each message. Each entry carries
/// its own notificationId — what the operator endpoints act on.
/// </summary>
public class OrderNotificationsEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/orders/{orderId}/notifications",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, ClaimsPrincipal user, IOrderNotificationService service, CancellationToken ct) =>
            {
                return await HandleAsync(orderId, user, service, ct);
            })
            .Produces<OrderNotificationsResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(int orderId, ClaimsPrincipal user, IOrderNotificationService service, CancellationToken ct)
    {
        var ownerId = user.GetUserId();
        if (string.IsNullOrEmpty(ownerId)) return Results.Unauthorized();

        var notifications = await service.GetOrderNotificationsAsync(orderId, ownerId, ct);
        if (notifications is null)
        {
            return Results.NotFound();
        }

        var response = new OrderNotificationsResponse
        {
            OrderId = orderId,
            Notifications = notifications
                .OrderBy(n => n.CreatedAt)
                .Select(NotificationDto.From)
                .ToList()
        };
        return Results.Ok(response);
    }
}

public class OrderNotificationsResponse
{
    public int OrderId { get; set; }
    public List<NotificationDto> Notifications { get; set; } = new();
}
