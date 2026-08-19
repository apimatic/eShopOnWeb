using System.Text.Json.Serialization;

namespace FirecrawlApi.Models;

/// <summary>
/// Inline diff artifact when the page changed. The shape depends on what the monitor's scrapeOptions.formats asked for. Markdown-only monitors populate both <c>text</c> (unified diff) and <c>json</c> (parseDiff AST). JSON-extraction monitors populate <c>json</c> as a per-field <c>{previous, current}</c> map keyed by JSON path. Mixed-mode monitors (<c>changeTracking</c> with both <c>json</c> and <c>git-diff</c> modes) populate both <c>text</c> (markdown sidecar) and <c>json</c> (per-field diff).
/// </summary>
public record Diff
{
    /// <summary>
    /// Unified markdown diff. Present on markdown-only and mixed-mode monitors.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("text")]
    public string? Text { get; init; }

    /// <summary>
    /// For markdown-only monitors, a parseDiff AST <c>{ files: [...] }</c>. For JSON-extraction (and mixed-mode) monitors, a per-field <c>{ previous, current }</c> map keyed by the JSON path into the extraction (e.g. <c>plans[0].price</c>).
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("json")]
    public object? Json { get; init; }
}
