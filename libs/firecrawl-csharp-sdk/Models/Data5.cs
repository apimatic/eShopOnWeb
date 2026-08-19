using System;
using System.Text.Json.Serialization;

namespace FirecrawlApi.Models;

public record Data5
{
    /// <summary>
    /// Number of credits remaining for the team
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("remainingCredits")]
    public double? RemainingCredits { get; init; }

    /// <summary>
    /// Number of credits in the plan. This does not include coupon credits, credit packs, or auto recharge credits.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("planCredits")]
    public double? PlanCredits { get; init; }

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
