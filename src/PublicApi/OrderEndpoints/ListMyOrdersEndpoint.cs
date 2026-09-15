using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using Microsoft.eShopWeb.PublicApi.Auth;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class ListMyOrdersEndpoint : IEndpoint<IResult, IOrderNotificationService>
{
    private readonly IReadRepository<OrderNotification> _notifications;

    public ListMyOrdersEndpoint(IReadRepository<OrderNotification> notifications)
    {
        _notifications = notifications;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal user, IOrderNotificationService orders) =>
            {
                return await HandleAsync(user, orders);
            })
            .Produces<ListMyOrdersResponse>()
            .WithTags("OrderEndpoints");
    }

    public Task<IResult> HandleAsync(IOrderNotificationService orders)
        => HandleAsync(new ClaimsPrincipal(), orders);

    public async Task<IResult> HandleAsync(ClaimsPrincipal user, IOrderNotificationService orders)
    {
        var buyerId = HttpUser.GetBuyerId(user);
        var placed = await orders.GetOrdersForBuyerAsync(buyerId);
        var notifications = await _notifications.ListAsync(new OrderNotificationsByBuyerSpec(buyerId));
        var byOrder = notifications.GroupBy(n => n.OrderId).ToDictionary(g => g.Key, g => g.ToList());

        var response = new ListMyOrdersResponse
        {
            Orders = placed.Select(order => new MyOrderDto
            {
                OrderId = order.Id,
                Status = order.Status.ToString(),
                OrderDate = order.OrderDate,
                Total = order.Total(),
                Notifications = byOrder.TryGetValue(order.Id, out var notes)
                    ? notes.Select(OrderNotificationDtoMapper.From).ToList()
                    : new()
            }).ToList()
        };

        return Results.Ok(response);
    }
}
