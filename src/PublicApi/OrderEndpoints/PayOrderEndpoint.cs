using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Models.Payments;
using Microsoft.eShopWeb.PublicApi.PaymentEndpointsShared;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// Authorizes (holds) the order total with a one-off card or a saved card.
/// The money is not taken until fulfilment. Repeating the call is safe.
/// </summary>
public class PayOrderEndpoint : IEndpoint<IResult, PayOrderRequest, ClaimsPrincipal, IOrderPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/pay",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, PayOrderRequest request, ClaimsPrincipal user, IOrderPaymentService orderPaymentService) =>
            {
                request.OrderId = orderId;
                return await HandleAsync(request, user, orderPaymentService);
            })
            .Produces<PayOrderResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(PayOrderRequest request, ClaimsPrincipal user, IOrderPaymentService orderPaymentService)
    {
        var buyerId = user.Identity?.Name;
        if (string.IsNullOrEmpty(buyerId))
        {
            return Results.Unauthorized();
        }

        PaymentSourceSelection source = (request.Card, request.PaymentMethodId) switch
        {
            (not null, null) => new PaymentSourceSelection.OneOffCard(request.Card!.ToModel()),
            (null, not null) => new PaymentSourceSelection.SavedCard(request.PaymentMethodId!.Value),
            _ => throw new PaymentRequestValidationException(
                "Provide exactly one payment source: either card details or a paymentMethodId.")
        };

        var payment = await orderPaymentService.PayOrderAsync(buyerId, request.OrderId, source);

        var response = new PayOrderResponse(request.CorrelationId())
        {
            OrderId = request.OrderId,
            OrderStatus = "PaymentAuthorized",
            Payment = PaymentDto.FromModel(payment)
        };
        return Results.Ok(response);
    }
}
