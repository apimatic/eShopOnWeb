using Microsoft.eShopWeb.PublicApi.PaymentEndpointsShared;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// Authorizes the order total. Provide either card (one-off payment) or
/// paymentMethodId (a saved card) — exactly one.
/// </summary>
public class PayOrderRequest : BaseRequest
{
    public int OrderId { get; set; }
    public CardDetailsRequest? Card { get; set; }
    public int? PaymentMethodId { get; set; }
}
