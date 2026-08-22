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

public class ListMyOrdersEndpoint : IEndpoint<IResult, HttpContext, IOrderMessagingService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (HttpContext httpContext, IOrderMessagingService service) =>
            {
                return await HandleAsync(httpContext, service);
            })
            .Produces<ListMyOrdersResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(HttpContext httpContext, IOrderMessagingService service)
    {
        var orders = await service.GetMyOrdersAsync(httpContext.GetRequiredBuyerId());
        var response = new ListMyOrdersResponse
        {
            Orders = orders.Select(entry => new ShopperOrderDto
            {
                OrderId = entry.Order.Id,
                Status = entry.Order.Status.ToString(),
                OrderDate = entry.Order.OrderDate,
                Total = entry.Order.Total(),
                Items = entry.Order.OrderItems.Select(item => new ShopperOrderItemDto
                {
                    CatalogItemId = item.ItemOrdered.CatalogItemId,
                    ProductName = item.ItemOrdered.ProductName,
                    Quantity = item.Units,
                    UnitPrice = item.UnitPrice
                }).ToList(),
                Notifications = entry.Notifications.Select(OrderNotificationDto.From).ToList()
            }).ToList()
        };

        return Results.Ok(response);
    }
}
