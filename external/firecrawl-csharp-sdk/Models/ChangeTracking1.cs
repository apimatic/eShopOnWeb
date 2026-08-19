using System;
using System.Text.Json.Serialization;
using FirecrawlApi.Models.Enums;

namespace FirecrawlApi.Models;

/// <summary>
/// Change tracking information if <c>changeTracking</c> is in <c>formats</c>. Only present when the <c>changeTracking</c> format is requested.
/// </summary>
public record ChangeTracking1
{
    /// <summary>
    /// The timestamp of the previous scrape that the current page is being compared against. Null if no previous scrape exists.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("previousScrapeAt")]
    public DateTimeOffset? PreviousScrapeAt { get; init; }

    /// <summary>
    /// The result of the comparison between the two page versions. 'new' means this page did not exist before, 'same' means content has not changed, 'changed' means content has changed, 'removed' means the page was removed.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("changeStatus")]
    public ChangeStatus? ChangeStatus { get; init; }

    /// <summary>
    /// The visibility of the current page/URL. 'visible' means the URL was discovered through an organic route (links or sitemap), 'hidden' means the URL was discovered through memory from previous crawls.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("visibility")]
    public Visibility? Visibility { get; init; }

    /// <summary>
    /// Git-style diff of changes when using 'git-diff' mode. Only present when the mode is set to 'git-diff'.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("diff")]
    public string? Diff { get; init; }

    /// <summary>
    /// JSON comparison results when using 'json' mode. Only present when the mode is set to 'json'. This will emit a list of all the keys and their values from the <c>previous</c> and <c>current</c> scrapes based on the type defined in the <c>schema</c>. Example <see href="/features/change-tracking">here</see>
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("json")]
    public object? Json { get; init; }
}
