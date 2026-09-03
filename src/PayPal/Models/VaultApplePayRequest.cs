using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using PayPal.Core.Models;

namespace PayPal.Models;

/// <summary>
/// A resource representing a request to vault Apple Pay.
/// </summary>
public record VaultApplePayRequest
{
    /// <summary>
    /// Encrypted Apple Pay token, containing card information. This token would be base64 encoded.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("token")]
    [StringLength(10000, MinimumLength = 1)]
    [RegularExpression("^.*$")]
    public string? Token { get; init; }

    /// <summary>
    /// The payment card to be used to fund a payment. Can be a credit or debit card.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("card")]
    public ApplePayRequestCard? Card { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
