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

public class GetMyOrdersEndpoint : IEndpoint<IResult, IOrderCheckoutService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (HttpContext http, IOrderCheckoutService checkout) =>
            {
                var orders = await checkout.GetMyOrdersAsync(http.User.GetBuyerId());
                return Results.Ok(new MyOrdersResponse
                {
                    Orders = orders.Select(o => OrderResponse.From(o, o.Currency ?? string.Empty)).ToList()
                });
            })
            .Produces<MyOrdersResponse>()
            .WithTags("OrderEndpoints");
    }

    public Task<IResult> HandleAsync(IOrderCheckoutService checkout) =>
        Task.FromResult(Results.BadRequest());
}

public class MyOrdersResponse
{
    public System.Collections.Generic.List<OrderResponse> Orders { get; set; } = new();
}
