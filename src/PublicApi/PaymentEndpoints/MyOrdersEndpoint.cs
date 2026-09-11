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

/// <summary>GET /api/my-orders — the caller's own orders with their payment state.</summary>
public class MyOrdersEndpoint : IEndpoint<IResult, IPaymentService, ClaimsPrincipal>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (IPaymentService paymentService, ClaimsPrincipal user) => await HandleAsync(paymentService, user))
            .WithTags("OrderPaymentEndpoints");
    }

    public async Task<IResult> HandleAsync(IPaymentService paymentService, ClaimsPrincipal user)
    {
        var buyerId = PaymentApi.BuyerId(user);
        if (string.IsNullOrEmpty(buyerId))
            return Results.Unauthorized();

        var result = await paymentService.GetMyOrdersAsync(buyerId);
        return PaymentApi.ToHttp(result, orders => new { orders = orders.Select(PaymentApi.OrderWithPaymentDto).ToArray() });
    }
}
