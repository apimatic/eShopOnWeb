using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class ListMyOrdersEndpoint : IEndpoint<IResult, IShopOrderService>
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
            async (HttpContext http, IShopOrderService service) =>
            {
                return await HandleAsync(http, service);
            })
            .Produces<ListMyOrdersResponse>()
            .WithTags("OrderEndpoints");
    }

    public Task<IResult> HandleAsync(IShopOrderService service) => HandleAsync(null!, service);

    private async Task<IResult> HandleAsync(HttpContext http, IShopOrderService service)
    {
        var buyerId = CallerIdentity.GetBuyerId(http.User);
        var orders = await service.ListBuyerOrdersAsync(buyerId);
        var notifications = await _notifications.ListForOrdersAsync(orders.Select(o => o.Id));
        var byOrder = notifications.GroupBy(n => n.OrderId).ToDictionary(g => g.Key, g => g.ToList());

        var response = new ListMyOrdersResponse
        {
            Orders = orders.Select(order => new MyOrderDto
            {
                OrderId = order.Id,
                Status = order.Status.ToString(),
                Total = order.Total(),
                OrderDate = order.OrderDate,
                Notifications = byOrder.TryGetValue(order.Id, out var notes)
                    ? notes.Select(OrderNotificationDto.From).ToList()
                    : new()
            }).ToList()
        };

        return Results.Ok(response);
    }
}
