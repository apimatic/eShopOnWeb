using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

public class SavePaymentMethodRequest : BaseRequest
{
    [JsonIgnore] public string BuyerId { get; set; } = "";

    public CardDetailsRequest Card { get; set; } = null!;
    public string? Alias { get; set; }
}
