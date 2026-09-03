using System.Text.Json.Serialization;
using Twilio.Core.Models;
using Twilio.Models.Enums;

namespace Twilio.Models;

public record ContentTranscription
{
    /// <summary>
    /// Content type discriminator.
    /// </summary>
    [JsonPropertyName("type")]
    public required Type21 Type { get; init; }

    /// <summary>
    /// Transcribed text.
    /// </summary>
    [JsonPropertyName("text")]
    public required string Text { get; init; }

    /// <summary>
    /// Transcription metadata.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("transcription")]
    public Transcription? Transcription { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
