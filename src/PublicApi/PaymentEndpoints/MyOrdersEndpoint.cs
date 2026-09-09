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

/// <summary>Returns the caller's own orders, each with its payment state.</summary>
public class MyOrdersEndpoint : IEndpoint<IResult, string, IPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal user, IPaymentService paymentService) =>
                await HandleAsync(user.GetBuyerId(), paymentService))
            .Produces<MyOrdersResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(string buyerId, IPaymentService paymentService)
    {
        var orders = await paymentService.GetOrdersForBuyerAsync(buyerId);
        var response = new MyOrdersResponse(System.Guid.NewGuid())
        {
            Orders = orders.Select(OrderSummaryResponse.From).ToList()
        };
        return Results.Ok(response);
    }
}
