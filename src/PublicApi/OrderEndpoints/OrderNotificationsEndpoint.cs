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
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class OrderNotificationsResponse
{
    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;

    /// <summary>Each entry carries its own notificationId, which the operator endpoints act on.</summary>
    public List<NotificationDto> Notifications { get; set; } = new();
}

/// <summary>
/// Returns what was sent for one of the caller's own orders and what became of each message.
/// </summary>
public class OrderNotificationsEndpoint : IEndpoint<IResult, int, IOrderNotificationService, ClaimsPrincipal>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/orders/{orderId}/notifications",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, IOrderNotificationService service, ClaimsPrincipal user) =>
                await HandleAsync(orderId, service, user))
            .Produces<OrderNotificationsResponse>()
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(int orderId, IOrderNotificationService service, ClaimsPrincipal user)
    {
        var userId = user.GetUserId();
        if (string.IsNullOrEmpty(userId))
        {
            return Results.Unauthorized();
        }

        var view = await service.GetOrderNotificationsAsync(orderId, userId);
        if (view is null)
        {
            return Results.NotFound();
        }

        return Results.Ok(new OrderNotificationsResponse
        {
            OrderId = view.Order.Id,
            Status = view.Order.Status.ToString(),
            Notifications = view.Notifications.Select(NotificationDto.FromEntity).ToList()
        });
    }
}
