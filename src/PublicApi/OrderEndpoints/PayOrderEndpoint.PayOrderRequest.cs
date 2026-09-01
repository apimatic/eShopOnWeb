using System.Text.Json.Serialization;
using Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// Authorizes the order total. Provide either <see cref="Card"/> for a one-off payment or
/// <see cref="SavedCardId"/> naming one of the caller's saved cards.
/// </summary>
public class PayOrderRequest : BaseRequest
{
    public CardDetailsDto? Card { get; set; }
    public int? SavedCardId { get; set; }

    /// <summary>Set from the route by the endpoint; never accepted from the request body.</summary>
    [JsonIgnore]
    public int OrderId { get; set; }

    /// <summary>Set from the JWT by the endpoint; never accepted from the request body.</summary>
    [JsonIgnore]
    public string BuyerId { get; set; } = string.Empty;
}
