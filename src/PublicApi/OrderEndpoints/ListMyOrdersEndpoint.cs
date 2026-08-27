using System.Linq;
using System.Security.Claims;
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

public class ListMyOrdersEndpoint : IEndpoint<IResult, ClaimsPrincipal, IRepository<Order>>
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
            async (ClaimsPrincipal user, IRepository<Order> orders) =>
                await HandleAsync(user, orders))
            .Produces<ListMyOrdersResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(ClaimsPrincipal user, IRepository<Order> orders)
    {
        var buyerId = user.GetRequiredBuyerId();
        var notifications = await _notifications.GetAndRefreshForBuyerAsync(buyerId);
        var customerOrders = await orders.ListAsync(new CustomerOrdersWithItemsSpecification(buyerId));
        var notificationsByOrder = notifications.GroupBy(n => n.OrderId).ToDictionary(g => g.Key, g => g.ToList());

        var response = new ListMyOrdersResponse();
        foreach (var order in customerOrders.OrderByDescending(o => o.OrderDate))
        {
            var dto = new MyOrderDto
            {
                OrderId = order.Id,
                Status = order.Status.ToString(),
                OrderDate = order.OrderDate,
                Total = order.Total()
            };

            if (notificationsByOrder.TryGetValue(order.Id, out var forOrder))
            {
                dto.Notifications.AddRange(forOrder.Select(NotificationDtoMapper.ToDto));
            }

            response.Orders.Add(dto);
        }

        return Results.Ok(response);
    }
}
