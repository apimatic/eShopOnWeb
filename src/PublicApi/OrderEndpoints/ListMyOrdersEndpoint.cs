using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.PublicApi.Extensions;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class ListMyOrdersEndpoint : IEndpoint<IResult, HttpContext, IOrderNotificationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (HttpContext httpContext, IOrderNotificationService orders) =>
            {
                return await HandleAsync(httpContext, orders);
            })
            .Produces<ListMyOrdersResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(HttpContext httpContext, IOrderNotificationService orders)
    {
        var buyerId = httpContext.GetBuyerId();
        var placed = await orders.ListBuyerOrdersAsync(buyerId);
        var notifications = await orders.ListNotificationsForOrdersAsync(placed.Select(o => o.Id));
        var byOrder = notifications.GroupBy(n => n.OrderId).ToDictionary(g => g.Key, g => g.ToList());

        var response = new ListMyOrdersResponse
        {
            Orders = placed.Select(order => new BuyerOrderDto
            {
                OrderId = order.Id,
                Status = order.Status.ToString(),
                Total = order.Total(),
                OrderDate = order.OrderDate,
                Notifications = byOrder.TryGetValue(order.Id, out var items)
                    ? items.Select(NotificationMapper.ToDto).ToList()
                    : new()
            }).ToList()
        };
        return Results.Ok(response);
    }
}
