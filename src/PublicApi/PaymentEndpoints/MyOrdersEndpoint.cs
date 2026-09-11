using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

/// <summary>The caller's own orders with their payment state.</summary>
public class MyOrdersEndpoint : IEndpoint<IResult, IPaymentService>
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public MyOrdersEndpoint(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (IPaymentService paymentService) =>
                await HandleAsync(paymentService))
            .WithTags("Orders");
    }

    public async Task<IResult> HandleAsync(IPaymentService paymentService)
    {
        var buyerId = _httpContextAccessor.HttpContext?.User.BuyerId();
        var orders = await paymentService.GetMyOrdersAsync(buyerId!);
        return Results.Ok(orders);
    }
}
