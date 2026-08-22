using System.Collections.Generic;
using System.Linq;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using Microsoft.eShopWeb.PublicApi.NotificationEndpoints;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class ListMyOrdersEndpoint : IEndpoint<IResult, IRepository<Order>>
{
    private readonly IOrderNotificationService _notifications;

    public ListMyOrdersEndpoint(IOrderNotificationService notifications)
    {
        _notifications = notifications;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (IRepository<Order> orders, HttpContext httpContext) =>
            {
                return await HandleAsync(orders, httpContext);
            })
            .Produces<ListMyOrdersResponse>()
            .WithTags("OrderEndpoints");
    }

    public Task<IResult> HandleAsync(IRepository<Order> orders)
        => HandleAsync(orders, null!);

    private async Task<IResult> HandleAsync(IRepository<Order> orders, HttpContext httpContext)
    {
        var buyerId = httpContext.GetBuyerId();
        if (string.IsNullOrEmpty(buyerId))
        {
            return Results.Unauthorized();
        }

        var myOrders = await orders.ListAsync(new CustomerOrdersWithItemsSpecification(buyerId), httpContext.RequestAborted);
        var notifications = await _notifications.ListForOrdersAsync(myOrders.Select(o => o.Id).ToList(), httpContext.RequestAborted);

        return Results.Ok(new ListMyOrdersResponse
        {
            Orders = myOrders.Select(order =>
            {
                notifications.TryGetValue(order.Id, out var notes);
                return new MyOrderDto
                {
                    OrderId = order.Id,
                    Status = order.FulfillmentStatus.ToString(),
                    OrderDate = order.OrderDate,
                    Total = order.Total(),
                    Notifications = (notes ?? Array.Empty<ApplicationCore.Entities.NotificationAggregate.OrderNotification>())
                        .Select(NotificationDto.From)
                        .ToList()
                };
            }).ToList()
        });
    }
}
