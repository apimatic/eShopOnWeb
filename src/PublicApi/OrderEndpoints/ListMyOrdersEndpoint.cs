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
    private readonly IHttpContextAccessor _httpContextAccessor;

    public ListMyOrdersEndpoint(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (IShopOrderService orders) =>
            {
                return await HandleAsync(orders);
            })
            .Produces<ListMyOrdersResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(IShopOrderService orders)
    {
        var buyerId = CallerIdentity.GetBuyerId(_httpContextAccessor.HttpContext!);
        if (string.IsNullOrEmpty(buyerId))
        {
            return Results.Unauthorized();
        }

        var myOrders = await orders.GetMyOrdersAsync(buyerId);
        var response = new ListMyOrdersResponse
        {
            Orders = myOrders.Select(order => new MyOrderDto
            {
                OrderId = order.OrderId,
                Status = order.Status.ToString(),
                OrderDate = order.OrderDate,
                Total = order.Total,
                Items = order.Items.Select(i => new OrderItemDto
                {
                    CatalogItemId = i.CatalogItemId,
                    ProductName = i.ProductName,
                    UnitPrice = i.UnitPrice,
                    Quantity = i.Units
                }).ToList(),
                Notifications = order.Notifications.Select(NotificationDto.From).ToList()
            }).ToList()
        };

        return Results.Ok(response);
    }
}
