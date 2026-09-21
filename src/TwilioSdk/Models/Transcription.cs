using System.Collections.Generic;
using System.Text.Json.Serialization;
using TwilioSdk.Core.Models;
using TwilioSdk.Core.Validation.Attributes;

namespace TwilioSdk.Models;

/// <summary>
/// Transcription metadata.
/// </summary>
public record Transcription
{
    /// <summary>
    /// Audio channel identifier (0 for inbound, 1 for outbound).
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("channel")]
    public int? Channel { get; init; }

    /// <summary>
    /// Overall confidence score for the transcription (0.0-1.0).
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("confidence")]
    [Minimum(0.0)]
    [Maximum(1.0)]
    public double? Confidence { get; init; }

    /// <summary>
    /// Transcription engine used.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("engine")]
    public string? Engine { get; init; }

    /// <summary>
    /// Word-level transcription data with timing information.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("words")]
    public IReadOnlyList<Word>? Words { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
