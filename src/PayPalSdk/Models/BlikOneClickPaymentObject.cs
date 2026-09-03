using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using PayPal.Core.Models;

namespace PayPal.Models;

/// <summary>
/// Information used to pay using BLIK one-click flow.
/// </summary>
public record BlikOneClickPaymentObject
{
    /// <summary>
    /// The merchant generated, unique reference serving as a primary identifier for accounts connected between Blik and a merchant.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("consumer_reference")]
    [StringLength(64, MinimumLength = 3)]
    [RegularExpression("^[ -~]{3,64}$")]
    public string? ConsumerReference { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
