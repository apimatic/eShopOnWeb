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
/// Authorizes (holds) the order total. The capture happens at fulfilment.
/// </summary>
public class PayOrderEndpoint : IEndpoint<IResult, PayOrderRequest, IPaymentService>
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
                (int orderId, PayOrderRequest request, IPaymentService paymentService) =>
                {
                    return await HandleAsync(request, paymentService);
                })
            .Produces<PayOrderResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(PayOrderRequest request, IPaymentService paymentService)
    {
        var buyerId = PaymentEndpointHelpers.RequireBuyerId(_httpContextAccessor);
        var orderId = PaymentEndpointHelpers.RequireOrderId(_httpContextAccessor);

        CardRequest? cardRequest = request.Card;
        if (cardRequest is null && !string.IsNullOrWhiteSpace(request.Number))
        {
            cardRequest = request;
        }

        var payment = await paymentService.PayOrderAsync(
            buyerId,
            orderId,
            cardRequest is null ? null : PaymentDtoMapper.ToCardDetails(cardRequest),
            request.PaymentMethodId,
            _httpContextAccessor.HttpContext?.RequestAborted ?? default);

        return Results.Ok(new PayOrderResponse
        {
            OrderId = orderId,
            Payment = PaymentDtoMapper.ToResponse(payment)
        });
    }
}
