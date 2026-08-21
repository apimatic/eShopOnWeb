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

public class ListMyOrdersEndpoint : IEndpoint<IResult, IOrderPlacementService>
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
            (IOrderPlacementService orders) =>
            {
                return await HandleAsync(orders);
            })
            .Produces<ListMyOrdersResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(IOrderPlacementService orders)
    {
        var myOrders = await orders.GetMyOrdersAsync(_httpContextAccessor.HttpContext!.GetBuyerId());
        var response = new ListMyOrdersResponse();
        response.Orders.AddRange(myOrders.Select(order => new ShopperOrderResponse
        {
            OrderId = order.OrderId,
            Status = order.Status,
            Total = order.Total,
            OrderDate = order.OrderDate,
            Notifications = order.Notifications.Select(NotificationDtoMapper.FromEntity).ToList()
        }));
        return Results.Ok(response);
    }
}
