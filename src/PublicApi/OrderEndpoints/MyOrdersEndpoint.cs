using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderNotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using Microsoft.eShopWeb.PublicApi.NotificationEndpoints;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class MyOrderItemDto
{
    public int CatalogItemId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public decimal UnitPrice { get; set; }
    public int Units { get; set; }
}

public class MyOrderDto
{
    public int OrderId { get; set; }
    public DateTimeOffset OrderDate { get; set; }
    public decimal Total { get; set; }
    public List<MyOrderItemDto> Items { get; set; } = new();

    /// <summary>Where each of this order's notifications got to.</summary>
    public List<NotificationDto> Notifications { get; set; } = new();
}

public class MyOrdersResponse
{
    public List<MyOrderDto> Orders { get; set; } = new();
}

/// <summary>Returns the signed-in shopper's own orders, each showing where its notifications got to.</summary>
public class MyOrdersEndpoint : IEndpoint<IResult, IReadRepository<Order>, IOrderNotificationService>
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public MyOrdersEndpoint(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (IReadRepository<Order> orderRepository, IOrderNotificationService notificationService) =>
                await HandleAsync(orderRepository, notificationService))
            .Produces<MyOrdersResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(IReadRepository<Order> orderRepository, IOrderNotificationService notificationService)
    {
        var buyerId = _httpContextAccessor.GetUserName();
        if (string.IsNullOrEmpty(buyerId))
        {
            return Results.Unauthorized();
        }

        var ct = _httpContextAccessor.RequestAborted();

        var orders = await orderRepository.ListAsync(new CustomerOrdersWithItemsSpecification(buyerId), ct);
        var notificationsByOrder = await notificationService.GetOwnerNotificationsByOrderAsync(buyerId, ct);

        var response = new MyOrdersResponse
        {
            Orders = orders.Select(order => new MyOrderDto
            {
                OrderId = order.Id,
                OrderDate = order.OrderDate,
                Total = order.Total(),
                Items = order.OrderItems.Select(i => new MyOrderItemDto
                {
                    CatalogItemId = i.ItemOrdered.CatalogItemId,
                    ProductName = i.ItemOrdered.ProductName,
                    UnitPrice = i.UnitPrice,
                    Units = i.Units
                }).ToList(),
                Notifications = GetOrderNotifications(notificationsByOrder, order.Id)
            }).ToList()
        };

        return Results.Ok(response);
    }

    private static List<NotificationDto> GetOrderNotifications(
        IReadOnlyDictionary<int, IReadOnlyList<OrderNotification>> notificationsByOrder, int orderId)
    {
        if (!notificationsByOrder.TryGetValue(orderId, out var notifications))
        {
            return new List<NotificationDto>();
        }

        return notifications.Select(NotificationDto.From).ToList();
    }
}
