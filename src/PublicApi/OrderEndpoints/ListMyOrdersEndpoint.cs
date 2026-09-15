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

public class ListMyOrdersEndpoint : IEndpoint<IResult, ClaimsPrincipal, IOrderCheckoutService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (ClaimsPrincipal user, IOrderCheckoutService checkout) =>
                await HandleAsync(user, checkout))
            .Produces<List<OrderResponse>>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(ClaimsPrincipal user, IOrderCheckoutService checkout)
    {
        var buyerId = CheckoutHttp.BuyerId(user);
        var orders = await checkout.ListMyOrdersAsync(buyerId);
        return Results.Ok(orders.Select(CheckoutHttp.ToResponse).ToList());
    }
}
