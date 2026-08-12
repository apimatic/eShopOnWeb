using System.Collections.Generic;
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

namespace Microsoft.eShopWeb.PublicApi.OrderNotificationEndpoints;

public class OrderNotificationsResponse
{
    public int OrderId { get; set; }
    public List<NotificationDto> Notifications { get; set; } = new();
}

/// <summary>
/// Lists what was sent for one of the shopper's own orders and what became of each message. Each entry
/// carries its own <c>notificationId</c> — the identifier the operator endpoints act on. Scoped to the
/// caller: another shopper's order is treated as not found.
/// </summary>
public class OrderNotificationsEndpoint : IEndpoint<IResult, int>
{
    private readonly IRepository<Order> _orders;
    private readonly IRepository<OrderNotification> _notifications;
    private readonly IOrderNotificationService _notificationService;
    private readonly IHttpContextAccessor _http;

    public OrderNotificationsEndpoint(
        IRepository<Order> orders,
        IRepository<OrderNotification> notifications,
        IOrderNotificationService notificationService,
        IHttpContextAccessor http)
    {
        _orders = orders;
        _notifications = notifications;
        _notificationService = notificationService;
        _http = http;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/orders/{orderId}/notifications",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId) => await HandleAsync(orderId))
            .Produces<OrderNotificationsResponse>()
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("OrderNotificationEndpoints");
    }

    public async Task<IResult> HandleAsync(int orderId)
    {
        var ct = _http.HttpContext!.RequestAborted;
        var ownerId = NotificationPresentation.CallerId(_http.HttpContext!.User);

        var order = await _orders.GetByIdAsync(orderId, ct);
        if (order == null || order.BuyerId != ownerId)
        {
            // Do not reveal the existence of another shopper's order.
            return Results.NotFound();
        }

        var notifications = (await _notifications.ListAsync(new OrderNotificationsByOrderSpecification(orderId), ct)).ToList();
        await _notificationService.RefreshStatusesAsync(notifications, ct);

        var response = new OrderNotificationsResponse
        {
            OrderId = orderId,
            Notifications = notifications.Select(NotificationDto.From).ToList()
        };
        return Results.Ok(response);
    }
}
