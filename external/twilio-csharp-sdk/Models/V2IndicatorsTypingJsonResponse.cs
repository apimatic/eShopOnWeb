using System.Text.Json.Serialization;
using Twilio.Core.Models;

namespace Twilio.Models;

public record V2IndicatorsTypingJsonResponse
{
    /// <summary>
    /// Indicates if the typing indicator was sent successfully.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("success")]
    public bool? Success { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
