using System.Text.Json.Serialization;

namespace FirecrawlApi.Models;

/// <summary>
/// Snapshot of the current JSON extraction at this run. Present on JSON-extraction and mixed-mode monitors; absent for markdown-only monitors.
/// </summary>
public record Snapshot
{
    /// <summary>
    /// The full structured JSON extracted on this run, matching the schema/prompt declared on the target's <c>changeTracking</c> format.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("json")]
    public object? Json { get; init; }
}
