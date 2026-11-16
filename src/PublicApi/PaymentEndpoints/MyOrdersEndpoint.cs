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

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

/// <summary>
/// Lists the caller's orders and their payment state.
/// </summary>
public class MyOrdersEndpoint : IEndpoint<IResult, ClaimsPrincipal, IPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-orders",
                [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
                (ClaimsPrincipal user, IPaymentService paymentService) =>
                    await HandleAsync(user, paymentService))
            .Produces<MyOrdersResponse>()
            .Produces(StatusCodes.Status401Unauthorized)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(ClaimsPrincipal user, IPaymentService paymentService)
    {
        var orders = await paymentService.GetMyOrdersAsync(user.GetBuyerId());

        return Results.Ok(new MyOrdersResponse
        {
            Orders = orders.Select(PaymentMapping.ToOrderResponse).ToList()
        });
    }
}
