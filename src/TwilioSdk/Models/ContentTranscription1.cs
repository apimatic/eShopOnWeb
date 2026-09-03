using System.Text.Json.Serialization;
using TwilioSdk.Core.Models;

namespace TwilioSdk.Models;

public record ContentTranscription1
{
    [JsonPropertyName("type")]
    public string Type { get; } = "TRANSCRIPTION";

    [JsonPropertyName("text")]
    public required string Text { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("transcription")]
    public Transcription1? Transcription { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
