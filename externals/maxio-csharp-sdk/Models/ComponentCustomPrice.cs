using System.Collections.Generic;
using System.Text.Json.Serialization;
using Maxio.Core.Models;
using Maxio.Models.Enums;

namespace Maxio.Models;

/// <summary>
/// Create or update custom pricing unique to the subscription. Used in place of <c>price_point_id</c>.
/// </summary>
public record ComponentCustomPrice
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
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("pricing_scheme")]
    public PricingScheme? PricingScheme { get; init; }

    /// <summary>
    /// The numerical interval. e.g., an interval of ‘30’ coupled with an interval_unit of day would mean this component price point would renew every 30 days. This property is only available for sites with Multifrequency enabled.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("interval")]
    public int? Interval { get; init; }

    /// <summary>
    /// A string representing the interval unit for this component price point, either month or day. This property is only available for sites with Multifrequency enabled.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("interval_unit")]
    public IntervalUnit? IntervalUnit { get; init; }

    /// <summary>
    /// (Optional) Id of the price point to use for list price calculations when
    /// overriding the customer price.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("list_price_point_id")]
    public int? ListPricePointId { get; init; }

    /// <summary>
    /// When true, list price calculations will continue to use the default price point even when a <c>custom_price</c> is supplied.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("use_default_list_price")]
    public bool? UseDefaultListPrice { get; init; }

    /// <summary>
    /// On/off components only need one price bracket starting at 1.
    /// </summary>
    [JsonPropertyName("prices")]
    public required IReadOnlyList<Price> Prices { get; init; }

    /// <summary>
    /// Applicable only to prepaid usage components. Controls whether the allocated quantity renews each period.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("renew_prepaid_allocation")]
    public bool? RenewPrepaidAllocation { get; init; }

    /// <summary>
    /// Applicable only to prepaid usage components. Controls whether remaining units roll over to the next period.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("rollover_prepaid_remainder")]
    public bool? RolloverPrepaidRemainder { get; init; }

    /// <summary>
    /// Applicable only when rollover is enabled. Number of <c>expiration_interval_unit</c>s after which rollover amounts expire.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("expiration_interval")]
    public int? ExpirationInterval { get; init; }

    /// <summary>
    /// Applicable only when rollover is enabled. Interval unit for rollover expiration (month or day).
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("expiration_interval_unit")]
    public ExpirationIntervalUnit? ExpirationIntervalUnit { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
