using System.Text.Json.Serialization;

namespace FirecrawlApi.Models;

public record Crawl402Error1
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("error")]
    public string? Error { get; init; }
}
