using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace PayPalServerSdk.Models;

/// <summary>
/// The pricing tier details.
/// </summary>
public record PricingTier
{
    /// <summary>
    /// The starting quantity for the tier.
    /// </summary>
    [JsonPropertyName("starting_quantity")]
    [StringLength(32, MinimumLength = 1)]
    [RegularExpression("^([0-9]+|([0-9]+)?[.][0-9]+)$")]
    public required string StartingQuantity { get; init; }

    /// <summary>
    /// The ending quantity for the tier. Optional for the last tier.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("ending_quantity")]
    [StringLength(32, MinimumLength = 1)]
    [RegularExpression("^([0-9]+|([0-9]+)?[.][0-9]+)$")]
    public string? EndingQuantity { get; init; }

    /// <summary>
    /// The currency and amount for a financial transaction, such as a balance or payment due.
    /// </summary>
    [JsonPropertyName("amount")]
    public required Money Amount { get; init; }
}
