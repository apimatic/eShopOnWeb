using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace FirecrawlApi.Models;

public record CrawlErrorsResponseObj
{
    /// <summary>
    /// Errored scrape jobs and error details
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("errors")]
    public IReadOnlyList<Error>? Errors { get; init; }

    /// <summary>
    /// List of URLs that were attempted in scraping but were blocked by robots.txt
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("robotsBlocked")]
    public IReadOnlyList<string>? RobotsBlocked { get; init; }
}
