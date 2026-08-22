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

public class ListMyOrdersEndpoint : IEndpoint<IResult, string, IOrderCheckoutService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (HttpContext httpContext, IOrderCheckoutService checkout) =>
            {
                return await HandleAsync(CreateOrderEndpoint.BuyerId(httpContext), checkout);
            })
            .Produces<ListOrdersResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(string buyerId, IOrderCheckoutService checkout)
    {
        var orders = await checkout.ListOrdersForBuyerAsync(buyerId);
        return Results.Ok(new ListOrdersResponse
        {
            Orders = orders.Select(OrderResponseMapper.Map).ToList()
        });
    }
}
