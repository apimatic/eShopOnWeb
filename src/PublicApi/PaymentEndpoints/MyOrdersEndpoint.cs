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

/// <summary>GET /api/my-orders — the caller's own orders with their payment state, newest first.</summary>
public class MyOrdersEndpoint : IEndpoint<IResult, IOrderPaymentService, HttpContext>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (IOrderPaymentService service, HttpContext http) =>
                await HandleAsync(service, http))
            .Produces<OrderResponse[]>()
            .WithTags("PaymentEndpoints");
    }

    public async Task<IResult> HandleAsync(IOrderPaymentService service, HttpContext http)
    {
        var buyerId = http.BuyerId();
        var orders = await service.GetMyOrdersAsync(buyerId, http.RequestAborted);
        return Results.Ok(orders.Select(o => o.ToResponse()).ToList());
    }
}
