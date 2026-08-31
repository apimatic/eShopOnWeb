using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// Authorizes (holds) the order total via PayPal, either with one-off card details
/// or with one of the shopper's saved cards. No money is taken yet.
/// </summary>
public class PayOrderEndpoint : IEndpoint<IResult, PayOrderRequest, IOrderPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/pay",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, PayOrderRequest request, IOrderPaymentService orderPaymentService, HttpContext httpContext) =>
            {
                request.OrderId = orderId;
                request.BuyerId = httpContext.User.FindFirstValue(ClaimTypes.Name) ?? string.Empty;
                return await HandleAsync(request, orderPaymentService);
            })
            .Produces<PayOrderResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(PayOrderRequest request, IOrderPaymentService orderPaymentService)
    {
        var response = new PayOrderResponse(request.CorrelationId());

        CardDetails? card = request.Card == null
            ? null
            : new CardDetails(
                request.Card.Number,
                request.Card.Expiry,
                request.Card.SecurityCode,
                request.Card.CardholderName,
                request.Card.BillingAddress == null
                    ? null
                    : new CardBillingAddress(
                        request.Card.BillingAddress.Line1,
                        request.Card.BillingAddress.Line2,
                        request.Card.BillingAddress.City,
                        request.Card.BillingAddress.State,
                        request.Card.BillingAddress.PostalCode,
                        request.Card.BillingAddress.CountryCode));

        var payment = await orderPaymentService.PayOrderAsync(request.BuyerId, request.OrderId, card, request.PaymentMethodId);

        response.OrderId = request.OrderId;
        response.Payment = PaymentDto.FromPayment(payment);
        return Results.Ok(response);
    }
}
