using System.Collections.Generic;
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
using Microsoft.eShopWeb.PublicApi.NotificationsFeature;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// Lists what was sent for an order and what became of each message. Each entry carries its own
/// notificationId — what the operator endpoints act on. Visible to the order's own shopper or
/// to an operator (an administrator), so an operator can find the message they need to act on.
/// </summary>
public class OrderNotificationsEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/orders/{orderId}/notifications",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, ClaimsPrincipal user, IRepository<Order> orderRepository, IOrderNotificationService notificationService) =>
                await HandleAsync(orderId, user, orderRepository, notificationService))
            .Produces<OrderNotificationsResponse>()
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("OrderEndpoints");
    }

    public static async Task<IResult> HandleAsync(
        int orderId,
        ClaimsPrincipal user,
        IRepository<Order> orderRepository,
        IOrderNotificationService notificationService)
    {
        var buyerId = user.GetBuyerId();
        if (string.IsNullOrEmpty(buyerId))
        {
            return Results.Unauthorized();
        }

        var order = await orderRepository.GetByIdAsync(orderId);
        if (order is null)
        {
            return Results.NotFound();
        }

        // A shopper may only see their own order's notifications; an operator may see any.
        if (order.BuyerId != buyerId && !user.IsAdministrator())
        {
            return Results.Forbid();
        }

        var notifications = await notificationService.GetNotificationsForOrderAsync(orderId);
        var items = notifications.Select(NotificationDto.From).ToList();
        return Results.Ok(new OrderNotificationsResponse(orderId, items));
    }
}

public record OrderNotificationsResponse(int OrderId, List<NotificationDto> Notifications);
