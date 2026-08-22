using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class ListMyOrdersEndpoint : IEndpoint<IResult, string?, IShopOrderService>
{
    private readonly IOrderNotificationService _notificationService;

    public ListMyOrdersEndpoint(IOrderNotificationService notificationService)
    {
        _notificationService = notificationService;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal user, IShopOrderService shopOrderService) =>
            {
                return await HandleAsync(user.GetBuyerId(), shopOrderService);
            })
            .Produces<ListMyOrdersResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(string? buyerId, IShopOrderService shopOrderService)
    {
        if (string.IsNullOrWhiteSpace(buyerId))
        {
            return Results.Unauthorized();
        }

        var orders = await shopOrderService.GetOrdersForBuyerAsync(buyerId);
        var notifications = await _notificationService.GetForOrdersAsync(orders.Select(o => o.Id));
        var byOrder = notifications.GroupBy(n => n.OrderId).ToDictionary(g => g.Key, g => g.ToList());

        var response = new ListMyOrdersResponse
        {
            Orders = orders.Select(order => new MyOrderDto
            {
                OrderId = order.Id,
                Status = order.Status.ToString(),
                Total = order.Total(),
                Notifications = byOrder.TryGetValue(order.Id, out var list)
                    ? list.Select(NotificationDto.From).ToList()
                    : new()
            }).ToList()
        };

        return Results.Ok(response);
    }
}
