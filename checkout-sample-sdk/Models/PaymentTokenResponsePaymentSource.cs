using System.Text.Json.Serialization;

namespace PayPalServerSdk.Models;

/// <summary>
/// The vaulted payment method details.
/// </summary>
public record PaymentTokenResponsePaymentSource
{
    /// <summary>
    /// Full representation of a Card Payment Token including network token.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("card")]
    public CardPaymentTokenEntity? Card { get; init; }

    /// <summary>
    /// Full representation of a PayPal Payment Token.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("paypal")]
    public PayPalPaymentToken? Paypal { get; init; }

    /// <summary>
    /// Full representation of a Venmo Payment Token.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("venmo")]
    public VenmoPaymentToken? Venmo { get; init; }

    /// <summary>
    /// A resource representing a response for Apple Pay.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("apple_pay")]
    public ApplePayPaymentToken? ApplePay { get; init; }
}
