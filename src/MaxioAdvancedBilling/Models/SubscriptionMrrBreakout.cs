using System.Text.Json.Serialization;
using MaxioAdvancedBilling.Core.Models;

namespace MaxioAdvancedBilling.Models;

public record SubscriptionMrrBreakout
{
    [JsonPropertyName("plan_amount_in_cents")]
    public required long PlanAmountInCents { get; init; }

    [JsonPropertyName("usage_amount_in_cents")]
    public required long UsageAmountInCents { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
