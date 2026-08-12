using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// GET /api/orders/{orderId}/notifications — what was sent for this order and what became of each
/// message. Each entry carries its own <c>notificationId</c> (what the operator endpoints act on).
/// Scoped to the caller's own order.
/// </summary>
public class OrderNotificationsEndpoint : IEndpoint<IResult, int, HttpContext, IReadRepository<Order>>
{
    private readonly IOrderNotificationService _orderNotifications;
    private readonly IReadRepository<OrderNotification> _notificationsRead;

    public OrderNotificationsEndpoint(IOrderNotificationService orderNotifications, IReadRepository<OrderNotification> notificationsRead)
    {
        _orderNotifications = orderNotifications;
        _notificationsRead = notificationsRead;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/orders/{orderId:int}/notifications",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, HttpContext http, IReadRepository<Order> orderRepository) =>
            {
                return await HandleAsync(orderId, http, orderRepository);
            })
            .Produces<OrderNotificationDto[]>()
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(int orderId, HttpContext http, IReadRepository<Order> orderRepository)
    {
        var buyerId = http.User.Identity?.Name;
        if (string.IsNullOrEmpty(buyerId)) return Results.Unauthorized();

        var order = await orderRepository.GetByIdAsync(orderId, http.RequestAborted);
        // Another shopper's order is simply not found — one shopper never sees another's.
        if (order is null || order.BuyerId != buyerId) return Results.NotFound();

        var notifications = await _notificationsRead.ListAsync(new OrderNotificationsByOrderSpecification(orderId), http.RequestAborted);
        await _orderNotifications.RefreshDeliveryOutcomesAsync(notifications, http.RequestAborted);

        var dtos = notifications.Select(OrderNotificationDto.From).ToArray();
        return Results.Ok(dtos);
    }
}
