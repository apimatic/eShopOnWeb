using Microsoft.eShopWeb.PublicApi.PaymentModels;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// Pays for an order. Supply EITHER <see cref="Card"/> for a one-off card payment OR
/// <see cref="PaymentMethodId"/> to pay with one of the shopper's saved cards — not both.
/// </summary>
public class PayOrderRequest : BaseRequest
{
    /// <summary>Set from the route; not taken from the request body.</summary>
    public int OrderId { get; set; }

    public CardRequest? Card { get; set; }

    public int? PaymentMethodId { get; set; }
}
