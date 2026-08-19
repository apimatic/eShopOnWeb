using System;
using System.Text.Json.Serialization;
using FirecrawlApi.Core.Validation;
using FirecrawlApi.Core.Validation.Attributes;
using FirecrawlApi.Models.Enums;

namespace FirecrawlApi.Models;

public record MonitorCheckPage
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("id")]
    public Guid? Id { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("targetId")]
    public string? TargetId { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("url")]
    [Format(FormatKind.Uri)]
    public string? Url { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("status")]
    public Status3? Status { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("previousScrapeId")]
    public Guid? PreviousScrapeId { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("currentScrapeId")]
    public Guid? CurrentScrapeId { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("statusCode")]
    public int? StatusCode { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("error")]
    public string? Error { get; init; }

    /// <summary>
    /// Extra per-page metadata. For search monitors this includes <c>searchStatus</c>, the finer-grained search disposition behind the top-level <c>status</c>: <c>alert</c> (maps to <c>new</c>), <c>already_seen</c>, <c>watching</c>, <c>ignored</c> (all map to <c>same</c>), or <c>skipped</c> (maps to <c>error</c>).
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("metadata")]
    public object? Metadata { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("judgment")]
    public MonitorPageJudgment? Judgment { get; init; }

    /// <summary>
    /// Inline diff artifact when the page changed. The shape depends on what the monitor's scrapeOptions.formats asked for. Markdown-only monitors populate both <c>text</c> (unified diff) and <c>json</c> (parseDiff AST). JSON-extraction monitors populate <c>json</c> as a per-field <c>{previous, current}</c> map keyed by JSON path. Mixed-mode monitors (<c>changeTracking</c> with both <c>json</c> and <c>git-diff</c> modes) populate both <c>text</c> (markdown sidecar) and <c>json</c> (per-field diff).
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("diff")]
    public Diff? Diff { get; init; }

    /// <summary>
    /// Snapshot of the current JSON extraction at this run. Present on JSON-extraction and mixed-mode monitors; absent for markdown-only monitors.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("snapshot")]
    public Snapshot? Snapshot { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("createdAt")]
    public DateTimeOffset? CreatedAt { get; init; }
}
