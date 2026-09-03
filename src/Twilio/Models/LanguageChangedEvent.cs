using System.Text.Json.Serialization;
using Twilio.Core.Models;

namespace Twilio.Models;

public record LanguageChangedEvent
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("tts_language_code")]
    public string? TtsLanguageCode { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("transcription_language_code")]
    public string? TranscriptionLanguageCode { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
