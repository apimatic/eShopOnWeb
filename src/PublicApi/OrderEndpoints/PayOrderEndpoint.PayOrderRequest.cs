using System.Text.Json.Serialization;
using Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class PayOrderRequest : BaseRequest
{
    /// <summary>Card details for a one-off payment. Omit when paying with a saved card.</summary>
    public CardDetailsRequest? Card { get; set; }

    /// <summary>Id of one of the caller's saved cards (from POST /api/payment-methods). Omit when paying with card details.</summary>
    public int? SavedCardId { get; set; }

    [JsonIgnore]
    public int OrderId { get; set; }

    [JsonIgnore]
    public string BuyerId { get; set; } = string.Empty;
}
