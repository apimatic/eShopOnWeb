using System.Text.Json.Serialization;
using Twilio.Core.Models;

namespace Twilio.Models;

public record LastTokenReceivedEvent
{
    /// <summary>
    /// Total number of tokens received.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("total_tokens")]
    public int? TotalTokens { get; init; }

    /// <summary>
    /// Total number of words received.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("total_words")]
    public int? TotalWords { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
