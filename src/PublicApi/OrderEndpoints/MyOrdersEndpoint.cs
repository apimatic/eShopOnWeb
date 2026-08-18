using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.Messaging;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// GET /api/my-orders — the caller's own orders, each showing where its notifications got to. Delivery
/// outcomes are refreshed from the provider on read (there is no callback URL for this app).
/// </summary>
public class MyOrdersEndpoint : IEndpoint<IResult, IRepository<Order>>
{
    private readonly IRepository<OrderNotification> _notificationRepository;
    private readonly IOrderNotificationService _notifications;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public MyOrdersEndpoint(
        IRepository<OrderNotification> notificationRepository,
        IOrderNotificationService notifications,
        IHttpContextAccessor httpContextAccessor)
    {
        _notificationRepository = notificationRepository;
        _notifications = notifications;
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (IRepository<Order> orderRepository) => await HandleAsync(orderRepository))
            .Produces<MyOrdersResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(IRepository<Order> orderRepository)
    {
        var buyerId = EndpointUser.Name(_httpContextAccessor);
        if (string.IsNullOrEmpty(buyerId))
            return Results.Unauthorized();

        var orders = await orderRepository.ListAsync(new CustomerOrdersWithItemsSpecification(buyerId));
        var orderIds = orders.Select(o => o.Id).ToList();

        var notifications = orderIds.Count == 0
            ? new List<OrderNotification>()
            : (await _notificationRepository.ListAsync(new NotificationsByOrderIdsSpecification(orderIds))).ToList();

        await _notifications.RefreshDeliveryStateAsync(notifications, CancellationToken.None);

        var byOrder = notifications.GroupBy(n => n.OrderId).ToDictionary(g => g.Key, g => g.ToList());

        var response = new MyOrdersResponse
        {
            Orders = orders.Select(o => new OrderSummaryDto
            {
                OrderId = o.Id,
                Status = o.Status.ToString(),
                OrderDate = o.OrderDate,
                Total = o.Total(),
                Items = o.OrderItems.Select(i => new OrderItemDto
                {
                    CatalogItemId = i.ItemOrdered.CatalogItemId,
                    ProductName = i.ItemOrdered.ProductName,
                    UnitPrice = i.UnitPrice,
                    Units = i.Units
                }).ToList(),
                Notifications = (byOrder.TryGetValue(o.Id, out var list) ? list : new List<OrderNotification>())
                    .Select(NotificationDto.From).ToList()
            }).ToList()
        };

        return Results.Ok(response);
    }
}
