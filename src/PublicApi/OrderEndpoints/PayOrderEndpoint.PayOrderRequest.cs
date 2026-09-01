using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class PayOrderRequest : BaseRequest
{
    [JsonIgnore]
    public int OrderId { get; set; }

    /// <summary>Card details for a one-off payment. Mutually exclusive with PaymentMethodId.</summary>
    public CardRequestDto? Card { get; set; }

    /// <summary>Id of a saved card (from POST api/payment-methods). Mutually exclusive with Card.</summary>
    public int? PaymentMethodId { get; set; }
}
