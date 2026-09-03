using System.Collections.Generic;
using System.Text.Json.Serialization;
using Maxio.Models.Enums;

namespace Maxio.Models;

/// <summary>
/// Custom pricing for a component within a scheduled renewal.
/// </summary>
public record ScheduledRenewalComponentCustomPrice
{
    /// <summary>
    /// Whether or not the price point includes tax
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("tax_included")]
    public bool? TaxIncluded { get; init; }

    /// <summary>
    /// Omit for On/Off components.
    /// </summary>
    [JsonPropertyName("pricing_scheme")]
    public required PricingScheme PricingScheme { get; init; }

    /// <summary>
    /// On/off components only need one price bracket starting at 1.
    /// </summary>
    [JsonPropertyName("prices")]
    public required IReadOnlyList<Price> Prices { get; init; }
}
