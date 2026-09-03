using System.Text.Json.Serialization;
using Maxio.Core.Models;

namespace Maxio.Models;

public record Breakouts
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("plan_amount_in_cents")]
    public long? PlanAmountInCents { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("plan_amount_formatted")]
    public string? PlanAmountFormatted { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("usage_amount_in_cents")]
    public long? UsageAmountInCents { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("usage_amount_formatted")]
    public string? UsageAmountFormatted { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
