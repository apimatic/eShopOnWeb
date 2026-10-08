using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// The card exactly as Adyen's checkout front end hands it over: encrypted fields plus the holder's name.
/// The shop never receives a plain card number.
/// </summary>
public class PayOrderRequest : BaseRequest
{
    public string? EncryptedCardNumber { get; set; }
    public string? EncryptedExpiryMonth { get; set; }
    public string? EncryptedExpiryYear { get; set; }
    public string? EncryptedSecurityCode { get; set; }
    public string? HolderName { get; set; }

    [JsonIgnore]
    public int OrderId { get; set; }

    [JsonIgnore]
    public string BuyerId { get; set; } = string.Empty;

    [JsonIgnore]
    public string ReturnUrl { get; set; } = string.Empty;

    // Never let card fields reach a log line through formatting.
    public override string ToString() => $"PayOrderRequest {{ OrderId = {OrderId} }}";
}
