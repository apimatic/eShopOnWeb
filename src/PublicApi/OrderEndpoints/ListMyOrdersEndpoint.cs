using System.Collections.Generic;
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

public class ListMyOrdersResponse
{
    public List<OrderResponse> Orders { get; set; } = new();
}

public class ListMyOrdersEndpoint : IEndpoint<IResult, IOrderCheckoutService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (IOrderCheckoutService checkout, ClaimsPrincipal user) =>
            {
                var orders = await checkout.GetMyOrdersAsync(user.GetBuyerId());
                return Results.Ok(new ListMyOrdersResponse
                {
                    Orders = orders.Select(OrderResponseMapper.From).ToList()
                });
            })
            .Produces<ListMyOrdersResponse>()
            .WithTags("OrderEndpoints");
    }

    public Task<IResult> HandleAsync(IOrderCheckoutService checkout) => Task.FromResult(Results.BadRequest());
}
