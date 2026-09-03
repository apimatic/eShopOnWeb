using System.Text.Json.Serialization;
using TwilioSdk.Core.Models;

namespace TwilioSdk.Models;

public record ContentTranscription
{
    /// <summary>
    /// Content type discriminator.
    /// </summary>
    [JsonPropertyName("type")]
    public string Type { get; } = "TRANSCRIPTION";

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
