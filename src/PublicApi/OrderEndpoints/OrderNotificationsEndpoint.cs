using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.PublicApi.NotificationEndpoints;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// What was sent for an order and what became of each message. Each entry carries its own
/// notificationId, which is what the operator endpoints act on. Visible to the order's owner, or to
/// an operator (administrator) for any order.
/// </summary>
public class OrderNotificationsEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/orders/{orderId}/notifications",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, ClaimsPrincipal user, IReadRepository<Order> orderRepository, IOrderNotificationService service) =>
                await HandleAsync(orderId, user, orderRepository, service))
            .Produces<System.Collections.Generic.IEnumerable<NotificationDto>>()
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("OrderEndpoints");
    }

    private static async Task<IResult> HandleAsync(int orderId, ClaimsPrincipal user, IReadRepository<Order> orderRepository, IOrderNotificationService service)
    {
        var callerId = user.Identity?.Name;
        if (string.IsNullOrEmpty(callerId))
        {
            return Results.Unauthorized();
        }

        var order = await orderRepository.GetByIdAsync(orderId);
        var isAdmin = user.IsInRole(BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS);

        // Owner-or-admin. A non-owner, non-admin is told 404 rather than 403 so order existence does not leak.
        if (order is null || (!isAdmin && order.BuyerId != callerId))
        {
            return Results.NotFound();
        }

        var notifications = await service.GetOrderNotificationsAsync(orderId);
        return Results.Ok(notifications.Select(NotificationDto.FromEntity));
    }
}
