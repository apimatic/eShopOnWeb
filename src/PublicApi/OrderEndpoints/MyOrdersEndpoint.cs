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
/// GET /api/my-orders — the caller's own orders, each showing where its notifications got to.
/// Delivery outcomes are refreshed from the provider's own record so the report is current.
/// </summary>
public class MyOrdersEndpoint : IEndpoint<IResult, HttpContext, IReadRepository<Order>>
{
    private readonly IOrderNotificationService _orderNotifications;
    private readonly IReadRepository<OrderNotification> _notificationsRead;

    public MyOrdersEndpoint(IOrderNotificationService orderNotifications, IReadRepository<OrderNotification> notificationsRead)
    {
        _orderNotifications = orderNotifications;
        _notificationsRead = notificationsRead;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (HttpContext http, IReadRepository<Order> orderRepository) =>
            {
                return await HandleAsync(http, orderRepository);
            })
            .Produces<OrderSummaryDto[]>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(HttpContext http, IReadRepository<Order> orderRepository)
    {
        var buyerId = http.User.Identity?.Name;
        if (string.IsNullOrEmpty(buyerId)) return Results.Unauthorized();

        var orders = await orderRepository.ListAsync(new CustomerOrdersWithItemsSpecification(buyerId), http.RequestAborted);
        var orderIds = orders.Select(o => o.Id).ToArray();

        var notifications = orderIds.Length == 0
            ? new System.Collections.Generic.List<OrderNotification>()
            : await _notificationsRead.ListAsync(new OrderNotificationsForOrdersSpecification(orderIds), http.RequestAborted);

        await _orderNotifications.RefreshDeliveryOutcomesAsync(notifications, http.RequestAborted);
        var byOrder = notifications.ToLookup(n => n.OrderId);

        var summaries = orders.Select(o => new OrderSummaryDto
        {
            OrderId = o.Id,
            OrderDate = o.OrderDate,
            Status = o.Status.ToString(),
            Total = o.Total(),
            ItemCount = o.OrderItems.Count,
            Notifications = byOrder[o.Id].Select(OrderNotificationDto.From).ToList()
        }).ToArray();

        return Results.Ok(summaries);
    }
}
