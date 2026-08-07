using System.Text.Json.Serialization;
using Microsoft.eShopWeb.PublicApi.PaymentModels;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// Pays an order. Provide EITHER <see cref="Card"/> (a one-off card) OR
/// <see cref="SavedPaymentMethodId"/> (one of your saved cards) — exactly one.
/// </summary>
public class PayOrderRequest : BaseRequest
{
    /// <summary>Set from the route, not the body.</summary>
    [JsonIgnore]
    public int OrderId { get; set; }

    /// <summary>Raw card details for a one-off payment.</summary>
    public CardInput? Card { get; set; }

    /// <summary>Id of one of the caller's saved cards to pay with instead.</summary>
    public int? SavedPaymentMethodId { get; set; }
}
