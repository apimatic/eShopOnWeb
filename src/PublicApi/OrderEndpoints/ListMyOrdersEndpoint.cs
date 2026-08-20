using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.PublicApi.NotificationEndpoints;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class ListMyOrdersEndpoint : IEndpoint<IResult, IShopperOrderService>
{
    private readonly IRepository<OrderNotification> _notifications;

    public ListMyOrdersEndpoint(IRepository<OrderNotification> notifications)
    {
        _notifications = notifications;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (IShopperOrderService shopperOrderService, ClaimsPrincipal user) =>
            {
                return await HandleAsync(shopperOrderService, user);
            })
            .Produces<ListMyOrdersResponse>()
            .WithTags("OrderEndpoints");
    }

    public Task<IResult> HandleAsync(IShopperOrderService shopperOrderService)
        => HandleAsync(shopperOrderService, null);

    private async Task<IResult> HandleAsync(IShopperOrderService shopperOrderService, ClaimsPrincipal? user)
    {
        var buyerId = BuyerIdentity.GetBuyerId(user ?? new ClaimsPrincipal());
        if (string.IsNullOrWhiteSpace(buyerId))
        {
            return Results.Unauthorized();
        }

        var orders = await shopperOrderService.ListMyOrdersAsync(buyerId, default);
        var notifications = await _notifications.ListAsync(new NotificationsByBuyerSpec(buyerId));
        var byOrder = notifications.GroupBy(n => n.OrderId).ToDictionary(g => g.Key, g => g.ToList());

        var response = new ListMyOrdersResponse
        {
            Orders = orders.Select(order => new MyOrderDto
            {
                OrderId = order.Id,
                Status = order.Status.ToString(),
                OrderDate = order.OrderDate.ToString("O"),
                Total = order.Total(),
                Notifications = byOrder.TryGetValue(order.Id, out var list)
                    ? list.Select(NotificationDto.From).ToList()
                    : new()
            }).ToList()
        };

        return Results.Ok(response);
    }
}
