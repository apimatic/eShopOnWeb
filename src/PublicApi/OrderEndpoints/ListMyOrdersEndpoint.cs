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

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class ListMyOrdersEndpoint : IEndpoint<IResult, ListMyOrdersRequest, IRepository<Order>>
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
                return await HandleAsync(new ListMyOrdersRequest { BuyerId = httpContext.GetBuyerId() }, orders);
            })
            .Produces<ListMyOrdersResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(ListMyOrdersRequest request, IRepository<Order> orders)
    {
        var response = new ListMyOrdersResponse(request.CorrelationId());
        var customerOrders = await orders.ListAsync(new CustomerOrdersWithItemsSpecification(request.BuyerId));
        var notifications = await _notifications.ListForOrdersAsync(
            customerOrders.Select(o => o.Id).ToList(),
            refreshFromProvider: true);

        var byOrder = notifications.GroupBy(n => n.OrderId).ToDictionary(g => g.Key, g => g.ToList());
        foreach (var order in customerOrders)
        {
            byOrder.TryGetValue(order.Id, out var orderNotifications);
            response.Orders.Add(OrderSummaryDto.From(order, orderNotifications ?? new List<OrderNotification>()));
        }

        return Results.Ok(response);
    }
}
