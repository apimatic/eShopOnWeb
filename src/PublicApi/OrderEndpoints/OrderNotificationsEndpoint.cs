using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
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
/// Lists what was sent for one of the caller's own orders and what became of each message. Each entry
/// carries its own notificationId — the identifier the operator endpoints act on.
/// </summary>
public class OrderNotificationsEndpoint : IEndpoint<IResult, int, ClaimsPrincipal>
{
    private readonly IReadRepository<Order> _orders;
    private readonly IReadRepository<Notification> _notifications;
    private readonly INotificationService _notificationService;

    public OrderNotificationsEndpoint(
        IReadRepository<Order> orders,
        IReadRepository<Notification> notifications,
        INotificationService notificationService)
    {
        _orders = orders;
        _notifications = notifications;
        _notificationService = notificationService;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/orders/{orderId}/notifications",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, ClaimsPrincipal user) =>
            {
                return await HandleAsync(orderId, user);
            })
            .Produces<List<NotificationDto>>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(int orderId, ClaimsPrincipal user)
    {
        var buyerId = user.GetBuyerId();
        if (string.IsNullOrEmpty(buyerId))
        {
            return Results.Unauthorized();
        }

        // Shopper-scoped: an order that isn't the caller's is treated as not found.
        var order = await _orders.GetByIdAsync(orderId);
        if (order is null || order.BuyerId != buyerId)
        {
            return Results.NotFound();
        }

        var notifications = await _notifications.ListAsync(new NotificationsByOrderSpecification(orderId));
        await _notificationService.RefreshStatusesAsync(notifications);

        var dtos = notifications.Select(n => n.ToDto()).ToList();
        return Results.Ok(dtos);
    }
}
