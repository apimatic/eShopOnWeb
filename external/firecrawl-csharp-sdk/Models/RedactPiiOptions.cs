using System.Collections.Generic;
using System.Text.Json.Serialization;
using FirecrawlApi.Models.Enums;

namespace FirecrawlApi.Models;

/// <summary>
/// Tuning options for PII redaction.
/// </summary>
public record RedactPiiOptions
{
    /// <summary>
    /// Redaction strategy. <c>accurate</c> is model-only and optimized for precision, <c>aggressive</c> increases recall with additional heuristics, and <c>fast</c> uses heuristics without the model call.
    /// </summary>
    [JsonPropertyName("mode")]
    public Mode2? Mode { get; init; } = Mode2.Accurate;

    /// <summary>
    /// Restrict redaction to these entity buckets. If omitted, all supported entities are redacted.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("entities")]
    public IReadOnlyList<RedactPiiEntity>? Entities { get; init; }

    /// <summary>
    /// <c>tag</c> replaces spans with placeholders like <c>&lt;EMAIL&gt;</c>, <c>mask</c> replaces characters with <c>*</c>, and <c>remove</c> deletes the span text.
    /// </summary>
    [JsonPropertyName("replaceStyle")]
    public ReplaceStyle? ReplaceStyle { get; init; } = ReplaceStyle.Tag;
}
