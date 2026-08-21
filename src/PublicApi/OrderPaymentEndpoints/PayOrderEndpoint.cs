using System.Globalization;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.PayPal;
using MinimalApi.Endpoint;
using System.Security.Claims;

namespace Microsoft.eShopWeb.PublicApi.OrderPaymentEndpoints;

/// <summary>Raw card details for a one-off card payment.</summary>
public class CardPayload
{
    public string Number { get; set; } = string.Empty;
    public int ExpiryMonth { get; set; }
    public int ExpiryYear { get; set; }
    public string SecurityCode { get; set; } = string.Empty;
    public string? CardholderName { get; set; }
}

public class PayOrderRequest : BaseRequest
{
    [JsonIgnore] public int OrderId { get; set; }
    [JsonIgnore] public string CallerId { get; set; } = string.Empty;

    /// <summary>Card details for a one-off payment, or null to pay with a saved card.</summary>
    public CardPayload? Card { get; set; }

    /// <summary>A saved card's id to pay with instead of raw card details.</summary>
    public int? SavedPaymentMethodId { get; set; }
}

public class PayOrderResponse : BaseResponse
{
    public PayOrderResponse(System.Guid correlationId) : base(correlationId) { }
    public PayOrderResponse() { }

    public OrderPaymentDto Payment { get; set; } = new();
}

/// <summary>
/// Authorizes (holds) the order total. The money is held, not taken. Idempotent: a double-click
/// never authorizes twice. Shopper-scoped — acts only on the caller's own order.
/// </summary>
public class PayOrderEndpoint : IEndpoint<IResult, PayOrderRequest, IOrderPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/pay",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, PayOrderRequest request, IOrderPaymentService service, ClaimsPrincipal user, CancellationToken ct) =>
            {
                request.OrderId = orderId;
                request.CallerId = user.GetUserName();
                return await HandleAsync(request, service, ct);
            })
            .Produces<PayOrderResponse>()
            .WithTags("OrderPaymentEndpoints");
    }

    public Task<IResult> HandleAsync(PayOrderRequest request, IOrderPaymentService service) =>
        HandleAsync(request, service, default);

    public async Task<IResult> HandleAsync(PayOrderRequest request, IOrderPaymentService service, CancellationToken ct)
    {
        PaymentInstrument instrument;
        if (request.SavedPaymentMethodId is { } savedId)
        {
            instrument = new PaymentInstrument { SavedPaymentMethodId = savedId };
        }
        else if (request.Card is { } card)
        {
            instrument = new PaymentInstrument
            {
                Card = new PayPalCardDetails
                {
                    Number = card.Number,
                    Expiry = $"{card.ExpiryYear.ToString("D4", CultureInfo.InvariantCulture)}-{card.ExpiryMonth.ToString("D2", CultureInfo.InvariantCulture)}",
                    SecurityCode = card.SecurityCode,
                    CardholderName = card.CardholderName
                }
            };
        }
        else
        {
            return Results.BadRequest(new { message = "Provide either card details or a saved payment method id to pay." });
        }

        var payment = await service.PayAsync(request.OrderId, instrument, request.CallerId, ct);

        return Results.Ok(new PayOrderResponse(request.CorrelationId())
        {
            Payment = PaymentDtoMapper.ToDto(payment)
        });
    }
}
