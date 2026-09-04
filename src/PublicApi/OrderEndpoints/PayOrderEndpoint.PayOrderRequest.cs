using Microsoft.eShopWeb.PublicApi.PaymentDtos;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// Authorizes (holds) the order total with exactly one payment source:
/// raw card details for a one-off payment, or a saved paymentMethodId.
/// </summary>
public class PayOrderRequest : BaseRequest
{
    public PaymentCardDto? Card { get; set; }

    public string? PaymentMethodId { get; set; }

    /// <summary>
    /// Optional guard: if set and it does not match the order total, the request is rejected
    /// before any money is held.
    /// </summary>
    public decimal? ExpectedAmount { get; set; }
}
