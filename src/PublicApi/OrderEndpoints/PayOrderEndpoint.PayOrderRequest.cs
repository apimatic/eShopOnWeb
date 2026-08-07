using System.Text.Json.Serialization;
using Microsoft.eShopWeb.PublicApi.Payments;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// Pays for an order with PayPal. Supply EITHER <see cref="Card"/> for a one-off payment OR
/// <see cref="SavedPaymentMethodId"/> to pay with one of the shopper's saved cards - not both.
/// </summary>
public class PayOrderRequest : BaseRequest
{
    /// <summary>Bound from the route, not the body.</summary>
    [JsonIgnore]
    public int OrderId { get; set; }

    public CardRequest? Card { get; set; }

    public int? SavedPaymentMethodId { get; set; }
}
