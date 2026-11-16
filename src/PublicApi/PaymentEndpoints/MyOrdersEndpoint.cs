using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.Services;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

/// <summary>
/// Lists the signed-in shopper's orders with their payment state.
/// </summary>
public class MyOrdersEndpoint : IEndpoint<IResult, MyOrdersRequest, IFinixPaymentService>
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
                (IFinixPaymentService paymentService) =>
                {
                    return await HandleAsync(new MyOrdersRequest(), paymentService);
                })
            .Produces<OrderDto[]>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(MyOrdersRequest request, IFinixPaymentService paymentService)
    {
        var buyerId = PaymentEndpointHelpers.GetBuyerId(_httpContextAccessor.HttpContext);
        var orders = await paymentService.GetMyOrdersAsync(buyerId);
        return Results.Ok(orders);
    }
}
