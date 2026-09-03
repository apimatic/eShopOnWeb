using System;
using System.Text.Json.Serialization;
using Twilio.Core.Models;

namespace Twilio.Models;

public record LastSimSwapInfo
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("last_sim_swap_date")]
    public DateTimeOffset? LastSimSwapDate { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("swapped_period")]
    public string? SwappedPeriod { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("swapped_in_period")]
    public bool? SwappedInPeriod { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
