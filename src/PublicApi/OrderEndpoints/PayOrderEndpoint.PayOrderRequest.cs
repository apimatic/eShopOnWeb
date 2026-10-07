using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// The card as Adyen's checkout front end hands it over: each field encrypted on the shopper's device. Accepted
/// either flat on the request or nested in <c>paymentMethod</c> (the shape the front end's state data uses).
/// </summary>
public class PayOrderRequest : BaseRequest
{
    public string? EncryptedCardNumber { get; set; }
    public string? EncryptedExpiryMonth { get; set; }
    public string? EncryptedExpiryYear { get; set; }
    public string? EncryptedSecurityCode { get; set; }
    public string? HolderName { get; set; }

    public PayOrderPaymentMethod? PaymentMethod { get; set; }

    [JsonIgnore]
    public int OrderId { get; set; }

    /// <summary>Taken from the bearer token, never from the body.</summary>
    [JsonIgnore]
    public string BuyerId { get; set; } = string.Empty;

    // Never print card data, even encrypted.
    public override string ToString() => $"PayOrderRequest {{ OrderId = {OrderId} }}";
}

public class PayOrderPaymentMethod
{
    public string? Type { get; set; }
    public string? EncryptedCardNumber { get; set; }
    public string? EncryptedExpiryMonth { get; set; }
    public string? EncryptedExpiryYear { get; set; }
    public string? EncryptedSecurityCode { get; set; }
    public string? HolderName { get; set; }

    public override string ToString() => "PayOrderPaymentMethod { *** }";
}
