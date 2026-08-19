using System.Text.Json.Serialization;
using FirecrawlApi.Models.Enums;

namespace FirecrawlApi.Models;

public record Scrape
{
    /// <summary>
    /// Scrape the current page content, returns the url and the html.
    /// </summary>
    [JsonPropertyName("type")]
    public required Type25 Type { get; init; }
}
