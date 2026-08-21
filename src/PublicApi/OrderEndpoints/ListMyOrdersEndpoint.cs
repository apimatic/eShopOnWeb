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

public class ListMyOrdersEndpoint : IEndpoint<IResult, ClaimsPrincipal, IShopOrderService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal user, IShopOrderService orders) =>
            {
                return await HandleAsync(user, orders);
            })
            .Produces<ListMyOrdersResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(ClaimsPrincipal user, IShopOrderService orders)
    {
        var list = await orders.ListBuyerOrdersAsync(ApiCaller.BuyerId(user), default);
        return Results.Ok(new ListMyOrdersResponse
        {
            Orders = list.Select(ApiCaller.ToDto).ToList()
        });
    }
}

public class ListMyOrdersResponse : BaseResponse
{
    public System.Collections.Generic.List<OrderDto> Orders { get; set; } = new();
}
