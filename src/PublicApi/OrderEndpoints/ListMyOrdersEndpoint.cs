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

public class ListMyOrdersEndpoint : IEndpoint<IResult, ListMyOrdersRequest, IShopperOrderService>
{
    private readonly IOrderNotificationService _notificationService;

    public ListMyOrdersEndpoint(IOrderNotificationService notificationService)
    {
        _notificationService = notificationService;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (HttpContext httpContext, IShopperOrderService service) =>
            {
                var unauthorized = BuyerIdentity.RequireBuyer(httpContext.User, out var buyerId);
                if (unauthorized is not null)
                {
                    return unauthorized;
                }

                return await HandleAsync(new ListMyOrdersRequest(buyerId), service);
            })
            .Produces<ListMyOrdersResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(ListMyOrdersRequest request, IShopperOrderService service)
    {
        var orders = await service.ListBuyerOrdersAsync(request.BuyerId);
        var notifications = await _notificationService.ListForBuyerAsync(request.BuyerId, refreshFromProvider: true);
        var notificationsByOrder = notifications.GroupBy(n => n.OrderId).ToDictionary(g => g.Key, g => g.Select(NotificationDto.From).ToList());

        var response = new ListMyOrdersResponse
        {
            Orders = orders.Select(order => new MyOrderDto
            {
                OrderId = order.Id,
                Status = order.Status.ToString(),
                OrderDate = order.OrderDate,
                Total = order.Total(),
                Items = order.OrderItems.Select(i => new MyOrderItemDto
                {
                    ProductName = i.ItemOrdered.ProductName,
                    Units = i.Units,
                    UnitPrice = i.UnitPrice
                }).ToList(),
                Notifications = notificationsByOrder.TryGetValue(order.Id, out var n) ? n : new()
            }).ToList()
        };

        return Results.Ok(response);
    }
}
