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

public class GetMyOrdersEndpoint : IEndpoint<IResult, IOrderNotificationService>
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public GetMyOrdersEndpoint(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (IOrderNotificationService service) =>
            {
                return await HandleAsync(service);
            })
            .Produces<GetMyOrdersResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(IOrderNotificationService service)
    {
        var http = _httpContextAccessor.HttpContext!;
        var buyerId = http.User.GetBuyerId();
        var orders = await service.ListBuyerOrdersAsync(buyerId, http.RequestAborted);
        var notifications = await service.ListNotificationsForOrdersAsync(orders.Select(o => o.Id).ToList(), http.RequestAborted);
        var byOrder = notifications.GroupBy(n => n.OrderId).ToDictionary(g => g.Key, g => g.Select(NotificationDto.From).ToList());

        var response = new GetMyOrdersResponse
        {
            Orders = orders.Select(order => new BuyerOrderDto
            {
                OrderId = order.Id,
                Status = order.Status.ToString(),
                Total = order.Total(),
                OrderDate = order.OrderDate,
                Items = order.OrderItems.Select(i => new BuyerOrderItemDto
                {
                    CatalogItemId = i.ItemOrdered.CatalogItemId,
                    ProductName = i.ItemOrdered.ProductName,
                    Units = i.Units,
                    UnitPrice = i.UnitPrice
                }).ToList(),
                Notifications = byOrder.TryGetValue(order.Id, out var list) ? list : new()
            }).ToList()
        };

        return Results.Ok(response);
    }
}
