using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// Card details as Adyen's checkout front end hands them over: the card number, expiry and security code are
/// encrypted on the shopper's device, so the shop never sees them in the clear.
/// </summary>
public class PayOrderRequest : BaseRequest
{
    public string EncryptedCardNumber { get; set; } = string.Empty;
    public string EncryptedExpiryMonth { get; set; } = string.Empty;
    public string EncryptedExpiryYear { get; set; } = string.Empty;
    public string EncryptedSecurityCode { get; set; } = string.Empty;
    public string HolderName { get; set; } = string.Empty;

    /// <summary>Set from the route, never from the body.</summary>
    [JsonIgnore]
    public int OrderId { get; set; }

    /// <summary>Set from the access token, never from the body.</summary>
    [JsonIgnore]
    public string BuyerId { get; set; } = string.Empty;
}
