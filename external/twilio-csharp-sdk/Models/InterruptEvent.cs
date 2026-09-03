using System.Text.Json.Serialization;
using Twilio.Core.Models;
using Twilio.Models.Enums;

namespace Twilio.Models;

public record InterruptEvent
{
    /// <summary>
    /// Type of interruption event.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("type")]
    public Type4? Type { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
