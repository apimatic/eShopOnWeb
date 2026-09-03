using System.Text.Json.Serialization;
using Twilio.Core.Models;

namespace Twilio.Models;

public record StudioV2FlowValidate
{
    /// <summary>
    /// Boolean if the flow definition is valid.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("valid")]
    public bool? Valid { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
