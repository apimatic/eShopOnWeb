using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

/// <summary>The signed-in shopper's orders with their payment state.</summary>
public class MyOrdersEndpoint : IEndpoint<IResult, IOrderCheckoutService, HttpContext>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (IOrderCheckoutService service, HttpContext http) =>
                await HandleAsync(service, http))
            .Produces<System.Collections.Generic.IReadOnlyList<OrderView>>()
            .WithTags("PaymentOrderEndpoints");
    }

    public async Task<IResult> HandleAsync(IOrderCheckoutService service, HttpContext http)
    {
        var buyerId = http.GetBuyerId();
        if (string.IsNullOrEmpty(buyerId)) return Results.Unauthorized();

        var orders = await service.GetOrdersAsync(buyerId, http.RequestAborted);
        return Results.Ok(orders);
    }
}
