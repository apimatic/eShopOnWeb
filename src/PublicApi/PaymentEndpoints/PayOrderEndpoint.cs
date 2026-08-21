using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

public class PayOrderRequest
{
    public int OrderId { get; set; }
    /// <summary>Card details for a one-off payment. Mutually exclusive with SavedPaymentMethodId.</summary>
    public CardModel? Card { get; set; }
    /// <summary>The id of one of the shopper's saved cards to pay with instead of a raw card.</summary>
    public int? SavedPaymentMethodId { get; set; }
}

public class PayOrderResponse
{
    public int OrderId { get; set; }
    public OrderPaymentDto Payment { get; set; } = new();
}

/// <summary>
/// POST /api/orders/{orderId}/pay — authorizes (holds) the order total. The money is not taken yet.
/// Pays with either card details or a saved card. Shopper-scoped; idempotent in effect.
/// </summary>
public class PayOrderEndpoint : IEndpoint<IResult, PayOrderRequest, IPaymentService, ClaimsPrincipal>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/pay",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, PayOrderRequest request, IPaymentService paymentService, ClaimsPrincipal user) =>
            {
                request.OrderId = orderId;
                return await HandleAsync(request, paymentService, user);
            })
            .Produces<PayOrderResponse>()
            .WithTags("PaymentEndpoints");
    }

    public async Task<IResult> HandleAsync(PayOrderRequest request, IPaymentService paymentService, ClaimsPrincipal user)
    {
        var buyerId = CallerIdentity.BuyerId(user);

        PaymentInstrument instrument;
        if (request.SavedPaymentMethodId is int savedId)
        {
            instrument = PaymentInstrument.FromVault(savedId.ToString());
        }
        else if (request.Card is not null)
        {
            instrument = PaymentInstrument.FromCard(request.Card.ToCardDetails());
        }
        else
        {
            throw new PaymentException("Provide either card details or a savedPaymentMethodId to pay.", PaymentErrorKind.Validation);
        }

        var payment = await paymentService.AuthorizeAsync(buyerId, request.OrderId, instrument);
        return Results.Ok(new PayOrderResponse { OrderId = payment.OrderId, Payment = PaymentMapper.ToDto(payment) });
    }
}
