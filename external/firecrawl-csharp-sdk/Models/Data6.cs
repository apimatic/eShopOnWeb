using System;
using System.Text.Json.Serialization;

namespace FirecrawlApi.Models;

public record Data6
{
    /// <summary>
    /// Number of tokens remaining for the team
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("remainingTokens")]
    public double? RemainingTokens { get; init; }

    /// <summary>
    /// Number of tokens in the plan. This does not include coupon tokens.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("planTokens")]
    public double? PlanTokens { get; init; }

    /// <summary>
    /// Start date of the current billing period.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("billingPeriodStart")]
    public DateTimeOffset? BillingPeriodStart { get; init; }

    /// <summary>
    /// End date of the current billing period.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("billingPeriodEnd")]
    public DateTimeOffset? BillingPeriodEnd { get; init; }
}
