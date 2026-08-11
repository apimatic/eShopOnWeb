using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.Payments.OrderEndpoints;

/// <summary>Raw card details for a one-off payment. Never stored or logged by this app.</summary>
public class CardInputDto
{
    public string Number { get; set; } = string.Empty;
    public string ExpiryMonth { get; set; } = string.Empty;
    public string ExpiryYear { get; set; } = string.Empty;
    public string SecurityCode { get; set; } = string.Empty;
    public string? CardholderName { get; set; }
    public string? BillingLine1 { get; set; }
    public string? BillingCity { get; set; }
    public string? BillingState { get; set; }
    public string? BillingPostalCode { get; set; }
    public string? BillingCountryCode { get; set; }
}

public class PayOrderRequest
{
    /// <summary>Card details for a one-off payment. Mutually exclusive with <see cref="SavedPaymentMethodId"/>.</summary>
    public CardInputDto? Card { get; set; }

    /// <summary>Id of one of the shopper's saved cards to pay with instead.</summary>
    public int? SavedPaymentMethodId { get; set; }
}

/// <summary>
/// POST /api/orders/{orderId}/pay — authorize (hold) the order total using card details or a
/// saved card. The money is not taken until fulfilment.
/// </summary>
public class PayOrderEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/pay",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, PayOrderRequest request, ClaimsPrincipal user, IOrderPaymentService service,
             CancellationToken ct) =>
            {
                var buyerId = user.GetBuyerId();

                CardDetails? card = request.Card is null ? null : new CardDetails(
                    request.Card.Number,
                    request.Card.ExpiryMonth,
                    request.Card.ExpiryYear,
                    request.Card.SecurityCode,
                    request.Card.CardholderName,
                    request.Card.BillingLine1,
                    request.Card.BillingCity,
                    request.Card.BillingState,
                    request.Card.BillingPostalCode,
                    request.Card.BillingCountryCode);

                var payment = await service.AuthorizeAsync(buyerId, orderId, card, request.SavedPaymentMethodId, ct);
                return Results.Ok(PaymentMapping.ToStateDto(payment));
            })
            .Produces<PaymentStateDto>()
            .WithTags("OrderPaymentEndpoints");
    }
}
