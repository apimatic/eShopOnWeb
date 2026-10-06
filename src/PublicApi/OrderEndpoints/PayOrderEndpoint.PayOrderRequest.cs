using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// The card as Adyen's checkout front end hands it over: encrypted fields plus the holder's name.
/// Accepted either flat on the request or nested under <c>paymentMethod</c> (the Drop-in/Components
/// <c>state.data.paymentMethod</c> shape). The shop never receives a plain card number.
/// </summary>
public class PayOrderRequest : BaseRequest
{
    public EncryptedCardDto? PaymentMethod { get; set; }

    public string? EncryptedCardNumber { get; set; }
    public string? EncryptedExpiryMonth { get; set; }
    public string? EncryptedExpiryYear { get; set; }
    public string? EncryptedSecurityCode { get; set; }
    public string? HolderName { get; set; }

    [JsonIgnore]
    public int OrderId { get; set; }

    /// <summary>Always taken from the caller's token, never from the request body.</summary>
    [JsonIgnore]
    public string BuyerId { get; set; } = string.Empty;

    // Keep card data out of logs: records/classes printed for diagnostics must never carry it.
    public override string ToString() => $"PayOrderRequest {{ OrderId = {OrderId} }}";
}

public class EncryptedCardDto
{
    public string? Type { get; set; }
    public string? EncryptedCardNumber { get; set; }
    public string? EncryptedExpiryMonth { get; set; }
    public string? EncryptedExpiryYear { get; set; }
    public string? EncryptedSecurityCode { get; set; }
    public string? HolderName { get; set; }

    public override string ToString() => "EncryptedCardDto { *** }";
}
