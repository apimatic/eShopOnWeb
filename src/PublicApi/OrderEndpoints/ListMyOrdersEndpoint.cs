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

public class ListMyOrdersEndpoint : IEndpoint<IResult, string?, IRepository<Order>>
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
                return await HandleAsync(httpContext.User.GetBuyerId(), orders);
            })
            .Produces<ListMyOrdersResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(string? buyerId, IRepository<Order> orders)
    {
        if (string.IsNullOrWhiteSpace(buyerId))
        {
            return Results.Unauthorized();
        }

        var myOrders = await orders.ListAsync(new CustomerOrdersWithItemsSpecification(buyerId));
        var response = new ListMyOrdersResponse();

        foreach (var order in myOrders.OrderByDescending(o => o.Id))
        {
            var notifications = await _notifications.ListForOrderAsync(order.Id, refreshFromProvider: true);
            response.Orders.Add(new MyOrderDto
            {
                OrderId = order.Id,
                Status = order.Status.ToString(),
                OrderDate = order.OrderDate,
                Total = order.Total(),
                Notifications = notifications.Select(NotificationDto.From).ToList()
            });
        }

        return Results.Ok(response);
    }
}
