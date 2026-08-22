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

public class ListMyOrdersEndpoint : IEndpoint<IResult, IShopperOrderService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (IShopperOrderService orders, IOrderNotificationService notifications, HttpContext http) =>
            {
                return await HandleAsync(orders, notifications, http);
            })
            .Produces<ListMyOrdersResponse>()
            .WithTags("OrderEndpoints");
    }

    public Task<IResult> HandleAsync(IShopperOrderService orders)
    {
        throw new System.NotSupportedException("Use the routed handler that supplies the current request services.");
    }

    private static async Task<IResult> HandleAsync(IShopperOrderService orders, IOrderNotificationService notifications, HttpContext http)
    {
        var buyerId = http.User.GetBuyerId();
        var shopperOrders = await orders.ListForBuyerAsync(buyerId);
        var response = new ListMyOrdersResponse();

        foreach (var order in shopperOrders)
        {
            var list = await notifications.ListForOrderAsync(order.Id, refreshFromProvider: true);
            response.Orders.Add(new MyOrderDto
            {
                OrderId = order.Id,
                Status = order.Status.ToString(),
                OrderDate = order.OrderDate,
                Total = order.Total(),
                Items = order.OrderItems.Select(i => new MyOrderItemDto
                {
                    CatalogItemId = i.ItemOrdered.CatalogItemId,
                    ProductName = i.ItemOrdered.ProductName,
                    UnitPrice = i.UnitPrice,
                    Units = i.Units
                }).ToList(),
                Notifications = list.Select(OrderNotificationDtoMapper.ToDto).ToList()
            });
        }

        return Results.Ok(response);
    }
}
