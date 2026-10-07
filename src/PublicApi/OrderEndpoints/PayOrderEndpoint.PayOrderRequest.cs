using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// The card exactly as Adyen's checkout front end hands it over: each field client-side encrypted.
/// </summary>
public class PayOrderRequest : BaseRequest
{
    public string EncryptedCardNumber { get; set; } = "";
    public string EncryptedExpiryMonth { get; set; } = "";
    public string EncryptedExpiryYear { get; set; } = "";
    public string EncryptedSecurityCode { get; set; } = "";
    public string HolderName { get; set; } = "";

    /// <summary>From the route.</summary>
    [JsonIgnore]
    public int OrderId { get; set; }

    /// <summary>Taken from the bearer token, never from the body.</summary>
    [JsonIgnore]
    public string BuyerId { get; set; } = "";
}
