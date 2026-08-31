using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class PayOrderRequest : BaseRequest
{
    [JsonIgnore]
    public int OrderId { get; set; }

    /// <summary>Raw card details for a one-off payment. Mutually exclusive with SavedPaymentMethodId.</summary>
    public CardDetailsDto? Card { get; set; }

    /// <summary>Pay with one of the shopper's saved cards instead of raw card details.</summary>
    public int? SavedPaymentMethodId { get; set; }
}
