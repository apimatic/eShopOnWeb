using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using PayPalServerSdk.Core.Models;

namespace PayPalServerSdk.Models;

/// <summary>
/// The tax details.
/// </summary>
public record Taxes
{
    /// <summary>
    /// The percentage, as a fixed-point, signed decimal number. For example, define a 19.99% interest rate as <c>19.99</c>.
    /// </summary>
    [JsonPropertyName("percentage")]
    [RegularExpression("^((-?[0-9]+)|(-?([0-9]+)?[.][0-9]+))$")]
    public required string Percentage { get; init; }

    /// <summary>
    /// Indicates whether the tax was already included in the billing amount.
    /// </summary>
    [JsonPropertyName("inclusive")]
    public bool? Inclusive { get; init; } = true;

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
