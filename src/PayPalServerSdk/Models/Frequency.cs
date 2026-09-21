using System.Text.Json.Serialization;
using PayPalServerSdk.Core.Models;
using PayPalServerSdk.Core.Validation.Attributes;
using PayPalServerSdk.Models.Enums;

namespace PayPalServerSdk.Models;

/// <summary>
/// The frequency of the billing cycle.
/// </summary>
public record Frequency
{
    /// <summary>
    /// The interval at which the subscription is charged or billed.
    /// </summary>
    [JsonPropertyName("interval_unit")]
    public required IntervalUnit IntervalUnit { get; init; }

    /// <summary>
    /// The number of intervals after which a subscriber is billed. For example, if the <c>interval_unit</c> is <c>DAY</c> with an <c>interval_count</c> of  <c>2</c>, the subscription is billed once every two days. The following table lists the maximum allowed values for the <c>interval_count</c> for each <c>interval_unit</c>: Interval unit Maximum interval count DAY 365 WEEK 52 MONTH 12 YEAR 1
    /// </summary>
    [JsonPropertyName("interval_count")]
    [Minimum(1)]
    [Maximum(365)]
    public int? IntervalCount { get; init; } = 1;

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
