using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class GetMyOrdersEndpoint : IEndpoint<IResult, string, IRepository<Order>>
{
    private readonly IOrderNotificationService _notifications;

    public GetMyOrdersEndpoint(IOrderNotificationService notifications)
    {
        _notifications = notifications;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (HttpContext httpContext, IRepository<Order> orders) =>
            {
                return await HandleAsync(httpContext.User.GetRequiredBuyerId(), orders);
            })
            .Produces<MyOrdersResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(string buyerId, IRepository<Order> orders)
    {
        var customerOrders = await orders.ListAsync(new CustomerOrdersWithItemsSpecification(buyerId));
        var notifications = await _notifications.GetForOrdersAsync(customerOrders.Select(order => order.Id).ToList(), refreshFromProvider: true);
        var notificationsByOrder = notifications
            .GroupBy(notification => notification.OrderId)
            .ToDictionary(group => group.Key, group => group.Select(NotificationDto.From).ToList());

        var response = new MyOrdersResponse
        {
            Orders = customerOrders.Select(order => new MyOrderDto
            {
                OrderId = order.Id,
                Status = order.Status.ToString(),
                OrderDate = order.OrderDate,
                Total = order.Total(),
                Items = order.OrderItems.Select(item => new MyOrderItemDto
                {
                    CatalogItemId = item.ItemOrdered.CatalogItemId,
                    ProductName = item.ItemOrdered.ProductName,
                    UnitPrice = item.UnitPrice,
                    Units = item.Units
                }).ToList(),
                Notifications = notificationsByOrder.TryGetValue(order.Id, out var orderNotifications)
                    ? orderNotifications
                    : new List<NotificationDto>()
            }).ToList()
        };

        return Results.Ok(response);
    }
}

public class MyOrdersResponse
{
    public List<MyOrderDto> Orders { get; set; } = new();
}

public class MyOrderDto
{
    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
    public System.DateTimeOffset OrderDate { get; set; }
    public decimal Total { get; set; }
    public List<MyOrderItemDto> Items { get; set; } = new();
    public List<NotificationDto> Notifications { get; set; } = new();
}

public class MyOrderItemDto
{
    public int CatalogItemId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public decimal UnitPrice { get; set; }
    public int Units { get; set; }
}
