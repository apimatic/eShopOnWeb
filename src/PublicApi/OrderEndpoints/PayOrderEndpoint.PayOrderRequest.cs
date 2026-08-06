using Microsoft.eShopWeb.PublicApi.PaymentModels;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// Pays for an order. Supply exactly one of: raw <see cref="Card"/> details for a one-off payment,
/// or <see cref="SavedPaymentMethodId"/> to pay with one of the shopper's saved cards.
/// </summary>
public class PayOrderRequest : BaseRequest
{
    /// <summary>Set from the route, not the body.</summary>
    public int OrderId { get; set; }

    public CardRequest? Card { get; set; }

    public int? SavedPaymentMethodId { get; set; }
}
