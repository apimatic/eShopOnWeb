using Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// Pays for an order. Provide either <see cref="Card"/> for a one-off payment
/// or <see cref="PaymentMethodId"/> of one of the caller's saved cards.
/// </summary>
public class PayOrderRequest : BaseRequest
{
    /// <summary>Set from the route, not the body.</summary>
    public int OrderId { get; set; }
    public CardDetailsRequest? Card { get; set; }
    public int? PaymentMethodId { get; set; }
}
