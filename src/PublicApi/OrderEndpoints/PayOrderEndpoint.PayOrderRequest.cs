using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class PayOrderRequest : BaseRequest
{
    [JsonIgnore]
    public int OrderId { get; set; }

    /// <summary>Card details for a one-off payment. Mutually exclusive with SavedPaymentMethodId.</summary>
    public CardDetailsDto? Card { get; set; }

    /// <summary>Id of one of the caller's saved cards (from POST api/payment-methods).</summary>
    public int? SavedPaymentMethodId { get; set; }

    /// <summary>Populated from the JWT; never read from the request body.</summary>
    [JsonIgnore]
    public string? BuyerId { get; set; }
}
