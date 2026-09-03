using System.Text.Json.Serialization;
using Twilio.Core.Models;
using Twilio.Core.Validation.Attributes;

namespace Twilio.Models;

public record StatusTimeouts1
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("inactive")]
    [Minimum(1)]
    public int? Inactive { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("closed")]
    [Minimum(1)]
    public int? Closed { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
