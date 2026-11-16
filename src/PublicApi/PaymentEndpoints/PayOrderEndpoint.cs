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
/// Authorizes (holds) the order total using a one-off card or a saved card.
/// </summary>
public class PayOrderEndpoint : IEndpoint<IResult, PayOrderRequest, IFinixPaymentService>
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public PayOrderEndpoint(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/pay",
                [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
                (int orderId, PayOrderRequest request, IFinixPaymentService paymentService) =>
                {
                    request.OrderId = orderId;
                    return await HandleAsync(request, paymentService);
                })
            .Produces<OrderDto>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(PayOrderRequest request, IFinixPaymentService paymentService)
    {
        var buyerId = PaymentEndpointHelpers.GetBuyerId(_httpContextAccessor.HttpContext);
        var order = await paymentService.PayOrderAsync(buyerId, request);
        return Results.Ok(order);
    }
}
