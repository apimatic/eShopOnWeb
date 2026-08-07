using Microsoft.eShopWeb.PublicApi.PaymentShared;

namespace Microsoft.eShopWeb.PublicApi.OrderPaymentEndpoints;

/// <summary>
/// Request to pay for an order. Supply EITHER <see cref="Card"/> (a one-off card) OR
/// <see cref="SavedPaymentMethodId"/> (one of the shopper's saved cards) — not both.
/// </summary>
public class PayOrderRequest
{
    public CardDto? Card { get; set; }

    /// <summary>The id of a saved card (from POST /api/payment-methods) to pay with.</summary>
    public int? SavedPaymentMethodId { get; set; }
}
