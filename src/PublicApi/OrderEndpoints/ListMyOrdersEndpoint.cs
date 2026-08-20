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
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (HttpContext http, IRepository<Order> orders) =>
            {
                return await HandleAsync(http, orders);
            })
            .Produces<ListMyOrdersResponse>()
            .WithTags("OrderEndpoints");
    }

    public Task<IResult> HandleAsync(IRepository<Order> orders)
        => HandleAsync(null!, orders);

    private async Task<IResult> HandleAsync(HttpContext http, IRepository<Order> orders)
    {
        var buyerId = http.GetRequiredBuyerId();
        var mine = await orders.ListAsync(new CustomerOrdersWithItemsSpecification(buyerId));
        var notifications = await _notifications.GetForBuyerOrdersAsync(mine.Select(o => o.Id).ToList());
        var byOrder = notifications.GroupBy(n => n.OrderId).ToDictionary(g => g.Key, g => g.ToList());

        var response = new ListMyOrdersResponse();
        foreach (var order in mine.OrderByDescending(o => o.Id))
        {
            byOrder.TryGetValue(order.Id, out var orderNotifications);
            response.Orders.Add(new OrderSummaryDto
            {
                OrderId = order.Id,
                Status = order.Status.ToString(),
                OrderDate = order.OrderDate,
                Total = order.Total(),
                Notifications = (orderNotifications ?? new()).Select(OrderNotificationDto.From).ToList()
            });
        }

        return Results.Ok(response);
    }
}
