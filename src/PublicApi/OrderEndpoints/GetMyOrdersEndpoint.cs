using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using System.Linq;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class GetMyOrdersResponse
{
    public List<ShopperOrderDto> Orders { get; set; } = new();
}

public class GetMyOrdersEndpoint : IEndpoint<IResult, HttpContext, IOrderLifecycleService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (HttpContext http, IOrderLifecycleService service) =>
            {
                return await HandleAsync(http, service);
            })
            .Produces<GetMyOrdersResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(HttpContext http, IOrderLifecycleService service)
    {
        var buyerId = http.RequireBuyerId();
        var orders = await service.GetMyOrdersAsync(buyerId);
        var notifications = await service.GetNotificationsForOrdersAsync(orders.Select(o => o.Id));
        var byOrder = notifications.GroupBy(n => n.OrderId).ToDictionary(g => g.Key, g => g.ToList());

        return Results.Ok(new GetMyOrdersResponse
        {
            Orders = orders.Select(order =>
            {
                byOrder.TryGetValue(order.Id, out var orderNotifications);
                return NotificationDtoFactory.From(order, orderNotifications ?? new List<OrderNotification>());
            }).ToList()
        });
    }
}
