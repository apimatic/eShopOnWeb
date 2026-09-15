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

public class ListMyOrdersEndpoint : IEndpoint<IResult, HttpContext>
{
    private readonly IReadRepository<Order> _orders;
    private readonly IOrderNotificationService _notifications;

    public ListMyOrdersEndpoint(IReadRepository<Order> orders, IOrderNotificationService notifications)
    {
        _orders = orders;
        _notifications = notifications;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (HttpContext httpContext) =>
            {
                return await HandleAsync(httpContext);
            })
            .Produces<ListMyOrdersResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(HttpContext httpContext)
    {
        var buyerId = httpContext.GetBuyerId();
        if (string.IsNullOrEmpty(buyerId))
        {
            return Results.Unauthorized();
        }

        var orders = await _orders.ListAsync(new CustomerOrdersWithItemsSpecification(buyerId));
        var notifications = await _notifications.ListForOrdersAsync(
            orders.Select(o => o.Id).ToList(),
            refreshFromProvider: true);

        var byOrderId = notifications.GroupBy(n => n.OrderId).ToDictionary(g => g.Key, g => g.ToList());

        var response = new ListMyOrdersResponse();
        foreach (var order in orders)
        {
            var dto = new MyOrderDto
            {
                OrderId = order.Id,
                Status = order.Status.ToString(),
                OrderDate = order.OrderDate,
                Total = order.Total()
            };

            if (byOrderId.TryGetValue(order.Id, out var orderNotifications))
            {
                dto.Notifications.AddRange(orderNotifications.Select(NotificationDto.From));
            }

            response.Orders.Add(dto);
        }

        return Results.Ok(response);
    }
}
