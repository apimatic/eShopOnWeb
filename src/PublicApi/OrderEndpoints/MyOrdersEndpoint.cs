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

/// <summary>The caller's own orders, each with its payment state. Never anyone else's.</summary>
public class MyOrdersEndpoint : IEndpoint<IResult, IPaymentService, HttpContext>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (IPaymentService paymentService, HttpContext context) =>
            {
                return await HandleAsync(paymentService, context);
            })
            .Produces<MyOrdersResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(IPaymentService paymentService, HttpContext context)
    {
        var buyerId = context.BuyerId();
        if (buyerId is null)
        {
            return Results.Unauthorized();
        }

        var orders = await paymentService.GetOrdersForBuyerAsync(buyerId, context.RequestAborted);

        return Results.Ok(new MyOrdersResponse { Orders = orders.ToList() });
    }
}
