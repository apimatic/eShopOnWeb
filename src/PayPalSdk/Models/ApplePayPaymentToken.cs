using System.Text.Json.Serialization;
using PayPal.Core.Models;

namespace PayPal.Models;

/// <summary>
/// A resource representing a response for Apple Pay.
/// </summary>
public record ApplePayPaymentToken
{
    /// <summary>
    /// The payment card to be used to fund a payment. Can be a credit or debit card.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("card")]
    public ApplePayCard? Card { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
