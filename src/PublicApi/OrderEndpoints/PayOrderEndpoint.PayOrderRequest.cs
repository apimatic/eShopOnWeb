using Microsoft.eShopWeb.PublicApi.PaymentModels;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// Request to pay for an order. Supply exactly one of <see cref="Card"/> (one-off card details) or
/// <see cref="SavedPaymentMethodId"/> (one of the shopper's saved cards).
/// </summary>
public class PayOrderRequest : BaseRequest
{
    /// <summary>One-off card details for this payment.</summary>
    public CardDto? Card { get; set; }

    /// <summary>Id of one of the shopper's saved cards to pay with instead of raw card details.</summary>
    public int? SavedPaymentMethodId { get; set; }

    /// <summary>Order id, set server-side from the route.</summary>
    public int OrderId { get; private set; }

    /// <summary>Owning shopper, set server-side from the bearer token.</summary>
    public string? BuyerId { get; private set; }

    public void SetRouteAndBuyer(int orderId, string buyerId)
    {
        OrderId = orderId;
        BuyerId = buyerId;
    }

    /// <summary>True when exactly one payment source (card or saved card) was supplied.</summary>
    public bool HasExactlyOnePaymentSource => (Card is not null) ^ SavedPaymentMethodId.HasValue;
}
