using System.Text.Json.Serialization;
using Twilio.Core.Models;

namespace Twilio.Models;

public record JobThresholds
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("error")]
    public double? Error { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
