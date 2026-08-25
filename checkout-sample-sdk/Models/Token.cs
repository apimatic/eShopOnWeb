using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using PayPalServerSdk.Models.Enums;

namespace PayPalServerSdk.Models;

/// <summary>
/// The tokenized payment source to fund a payment.
/// </summary>
public record Token
{
    /// <summary>
    /// The PayPal-generated ID for the token.
    /// </summary>
    [JsonPropertyName("id")]
    [StringLength(255, MinimumLength = 1)]
    [RegularExpression("^[0-9a-zA-Z_-]+$")]
    public required string Id { get; init; }

    /// <summary>
    /// The tokenization method that generated the ID.
    /// </summary>
    [JsonPropertyName("type")]
    public required TokenType Type { get; init; }
}
