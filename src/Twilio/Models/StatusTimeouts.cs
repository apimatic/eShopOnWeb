using System.Text.Json.Serialization;
using Twilio.Core.Models;
using Twilio.Core.Validation.Attributes;

namespace Twilio.Models;

public record StatusTimeouts
{
    /// <summary>
    /// The inactivity timeout in minutes. For more information, see <see href="/docs/platform/conversations/concepts/lifecycle">Conversation lifecycle</see>.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("inactive")]
    [Minimum(1)]
    public int? Inactive { get; init; }

    /// <summary>
    /// The close timeout in minutes. For more information, see <see href="/docs/platform/conversations/concepts/lifecycle">Conversation lifecycle</see>.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("closed")]
    [Minimum(1)]
    public int? Closed { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
