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
/// Authorizes (holds) the order total. The money is held, not taken. The shopper pays with one-off
/// card details or one of their saved cards. Idempotent in effect.
/// </summary>
public class PayOrderEndpoint : IEndpoint<IResult, PayOrderRequest, HttpContext, IOrderPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/pay",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, PayOrderRequest request, HttpContext http, IOrderPaymentService service) =>
            {
                request.OrderId = orderId;
                return await HandleAsync(request, http, service);
            })
            .Produces<PayOrderResponse>()
            .WithTags("PaymentEndpoints");
    }

    public Task<IResult> HandleAsync(PayOrderRequest request, HttpContext http, IOrderPaymentService service) =>
        PaymentApiHelpers.RunAsync(http, async buyerId =>
        {
            var card = request.Card?.ToCardDetails();
            var outcome = await service.PayAsync(buyerId, request.OrderId, card, request.PaymentMethodId, http.RequestAborted);

            var response = new PayOrderResponse(request.CorrelationId())
            {
                OrderId = request.OrderId,
                PaymentStatus = outcome.PaymentStatus.ToString(),
                PayPalOrderId = outcome.PayPalOrderId,
                AuthorizationId = outcome.AuthorizationId,
                AuthorizationStatus = outcome.AuthorizationStatus,
                Amount = outcome.Amount,
                Currency = outcome.CurrencyCode
            };
            return Results.Ok(response);
        });
}
