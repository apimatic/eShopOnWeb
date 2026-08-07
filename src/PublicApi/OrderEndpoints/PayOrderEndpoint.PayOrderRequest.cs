using Microsoft.eShopWeb.PublicApi.PaymentModels;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// Pays an order. Supply EITHER <see cref="Card"/> for a one-off payment OR <see cref="PaymentMethodId"/>
/// to charge one of the shopper's saved cards — exactly one, not both.
/// </summary>
public class PayOrderRequest : BaseRequest
{
    /// <summary>Set from the route; any value supplied in the body is ignored.</summary>
    public int OrderId { get; set; }

    public CardRequest? Card { get; set; }

    public int? PaymentMethodId { get; set; }
}
