using System.Text.Json.Serialization;
using Microsoft.eShopWeb.PublicApi.PaymentShared;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// Pays for an order with PayPal, using either one-off <see cref="Card"/> details or one of the
/// shopper's saved cards named by <see cref="SavedPaymentMethodId"/>. Provide exactly one.
/// </summary>
public class PayOrderRequest : BaseRequest
{
    /// <summary>Card details for a one-off payment. Omit when paying with a saved card.</summary>
    public CardRequest? Card { get; set; }

    /// <summary>Id of a saved card (from <c>POST /api/payment-methods</c>). Omit when paying with card details.</summary>
    public int? SavedPaymentMethodId { get; set; }

    /// <summary>Set server-side from the route; never bound from the request body.</summary>
    [JsonIgnore]
    public int OrderId { get; set; }

    /// <summary>Set server-side from the JWT; never bound from the request body.</summary>
    [JsonIgnore]
    public string BuyerId { get; set; } = string.Empty;
}
