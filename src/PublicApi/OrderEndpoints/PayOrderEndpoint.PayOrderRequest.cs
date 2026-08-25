using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class PayOrderRequest : BaseRequest
{
    [JsonIgnore] public string BuyerId { get; set; } = "";
    [JsonIgnore] public int OrderId { get; set; }

    /// <summary>One-off card details. Mutually exclusive with PaymentMethodId.</summary>
    public CardDetailsRequest? Card { get; set; }

    /// <summary>A previously saved card belonging to the caller. Mutually exclusive with Card.</summary>
    public int? PaymentMethodId { get; set; }
}
