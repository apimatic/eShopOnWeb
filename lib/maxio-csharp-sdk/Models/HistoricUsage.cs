using System;
using System.Text.Json.Serialization;
using Maxio.Core.Models;

namespace Maxio.Models;

/// <summary>
/// (Optional) For Event Based Components. If the <c>include=historic_usages</c> query param is provided, the last ten billing periods will be returned.
/// </summary>
public record HistoricUsage
{
    /// <summary>
    /// Total usage of a component for billing period
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("total_usage_quantity")]
    public double? TotalUsageQuantity { get; init; }

    /// <summary>
    /// Start date of billing period
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("billing_period_starts_at")]
    public DateTimeOffset? BillingPeriodStartsAt { get; init; }

    /// <summary>
    /// End date of billing period
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("billing_period_ends_at")]
    public DateTimeOffset? BillingPeriodEndsAt { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
