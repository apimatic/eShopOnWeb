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

public class ListMyOrdersEndpoint : IEndpoint<IResult, ICatalogOrderService>
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
            async (ClaimsPrincipal user, ICatalogOrderService orders) =>
            {
                var buyerId = user.GetBuyerId();
                if (string.IsNullOrEmpty(buyerId))
                {
                    return Results.Unauthorized();
                }

                return await HandleAsync(buyerId, orders);
            })
            .Produces<ListMyOrdersResponse>()
            .WithTags("OrderEndpoints");
    }

    public Task<IResult> HandleAsync(ICatalogOrderService orders)
        => HandleAsync(string.Empty, orders);

    private async Task<IResult> HandleAsync(string buyerId, ICatalogOrderService orders)
    {
        var buyerOrders = await orders.ListBuyerOrdersAsync(buyerId);
        var notifications = await _notifications.ListForBuyerAsync(buyerId);
        var byOrder = notifications.GroupBy(n => n.OrderId).ToDictionary(g => g.Key, g => g.ToList());

        var response = new ListMyOrdersResponse
        {
            Orders = buyerOrders.Select(order => new OrderSummaryDto
            {
                OrderId = order.Id,
                Status = order.Status,
                OrderDate = order.OrderDate,
                Total = order.Total(),
                Notifications = byOrder.TryGetValue(order.Id, out var items)
                    ? items.Select(NotificationDtoMapper.ToDto).ToList()
                    : new()
            }).ToList()
        };

        return Results.Ok(response);
    }
}
