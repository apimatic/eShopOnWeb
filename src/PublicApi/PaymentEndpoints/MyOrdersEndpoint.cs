using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

/// <summary>Returns the signed-in shopper's own orders with their payment state.</summary>
public class MyOrdersEndpoint : IEndpoint<IResult, HttpContext, IOrderPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (HttpContext http, IOrderPaymentService service) =>
                await HandleAsync(http, service))
            .Produces<MyOrdersResponse>()
            .WithTags("PaymentEndpoints");
    }

    public Task<IResult> HandleAsync(HttpContext http, IOrderPaymentService service) =>
        PaymentApiHelpers.RunAsync(http, async buyerId =>
        {
            var orders = await service.GetOrdersForBuyerAsync(buyerId, http.RequestAborted);

            var response = new MyOrdersResponse
            {
                Orders = orders.Select(OrderSummaryMapper.Map).ToList()
            };
            return Results.Ok(response);
        });
}
