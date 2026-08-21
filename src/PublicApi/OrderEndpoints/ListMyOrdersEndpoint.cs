using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class ListMyOrdersEndpoint : IEndpoint<IResult, HttpContext, ICatalogOrderService>
{
    private readonly IOrderNotificationService _notifications;

    public ListMyOrdersEndpoint(IOrderNotificationService notifications)
    {
        _notifications = notifications;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (HttpContext httpContext, ICatalogOrderService orders) =>
            {
                return await HandleAsync(httpContext, orders);
            })
            .Produces<ListMyOrdersResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(HttpContext httpContext, ICatalogOrderService orders)
    {
        var buyerOrders = await orders.ListForBuyerAsync(httpContext.GetBuyerId());
        var notifications = await _notifications.ListForOrdersAsync(buyerOrders.Select(o => o.Id), refreshFromProvider: true);
        var byOrder = notifications.GroupBy(n => n.OrderId).ToDictionary(g => g.Key, g => g.ToList());

        var response = new ListMyOrdersResponse();
        foreach (var order in buyerOrders)
        {
            byOrder.TryGetValue(order.Id, out var orderNotifications);
            response.Orders.Add(new MyOrderDto
            {
                OrderId = order.Id,
                Status = order.Status.ToString(),
                OrderDate = order.OrderDate,
                Total = order.Total(),
                Notifications = (orderNotifications ?? new List<ApplicationCore.Entities.NotificationAggregate.OrderNotification>())
                    .Select(NotificationDto.FromEntity)
                    .ToList()
            });
        }

        return Results.Ok(response);
    }
}

public class ListMyOrdersResponse : BaseResponse
{
    public List<MyOrderDto> Orders { get; set; } = new();
}

public class MyOrderDto
{
    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTimeOffset OrderDate { get; set; }
    public decimal Total { get; set; }
    public List<NotificationDto> Notifications { get; set; } = new();
}
