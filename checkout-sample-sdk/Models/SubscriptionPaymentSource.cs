using System.Text.Json.Serialization;

namespace PayPalServerSdk.Models;

/// <summary>
/// The payment source definition. To be eligible to create subscription using debit or credit card, you will need to sign up here (https://www.paypal.com/bizsignup/entry/product/ppcp). Please note, its available only for non-3DS cards and for merchants in US and AU regions.
/// </summary>
public record SubscriptionPaymentSource
{
    /// <summary>
    /// The payment card to use to fund a payment. Can be a credit or debit card.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("card")]
    public SubscriptionCardRequest? Card { get; init; }
}
