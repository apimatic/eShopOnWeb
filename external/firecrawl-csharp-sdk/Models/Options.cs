using System.Text.Json.Serialization;

namespace FirecrawlApi.Models;

/// <summary>
/// The crawler options used for this crawl
/// </summary>
public record Options
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("scrapeOptions")]
    public ScrapeOptions? ScrapeOptions { get; init; }
}
