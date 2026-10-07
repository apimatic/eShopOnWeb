using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// The card as Adyen's checkout front end hands it over: each card field encrypted client-side
/// (in Adyen's test environment, a plain value prefixed with <c>test_</c>).
/// </summary>
public class PayOrderRequest : BaseRequest
{
    [JsonIgnore]
    public int OrderId { get; set; }

    [JsonIgnore]
    public string BuyerId { get; set; } = string.Empty;

    [JsonIgnore]
    public string ReturnUrl { get; set; } = string.Empty;

    public string? EncryptedCardNumber { get; set; }
    public string? EncryptedExpiryMonth { get; set; }
    public string? EncryptedExpiryYear { get; set; }
    public string? EncryptedSecurityCode { get; set; }
    public string? HolderName { get; set; }

    // Card data must never reach a log through string formatting.
    public override string ToString() => $"PayOrderRequest {{ OrderId = {OrderId}, card = [redacted] }}";
}
