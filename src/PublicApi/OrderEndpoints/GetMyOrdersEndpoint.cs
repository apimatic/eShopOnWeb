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

public class GetMyOrdersEndpoint : IEndpoint<IResult, string, ICheckoutPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (ICheckoutPaymentService checkout, ClaimsPrincipal user) =>
            {
                var orders = await checkout.GetMyOrdersAsync(OrderEndpointHelpers.GetBuyerId(user));
                return Results.Ok(new MyOrdersResponse
                {
                    Orders = orders.Select(OrderEndpointHelpers.ToDto).ToList()
                });
            })
            .Produces<MyOrdersResponse>()
            .WithTags("OrderEndpoints");
    }

    public Task<IResult> HandleAsync(string request, ICheckoutPaymentService checkout) =>
        Task.FromResult(Results.BadRequest());
}
