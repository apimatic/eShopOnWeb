using System.Text.Json.Serialization;

namespace FirecrawlApi.Models;

public record Scrape1
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("url")]
    public string? Url { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("html")]
    public string? Html { get; init; }
}
