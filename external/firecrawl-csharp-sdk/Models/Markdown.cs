using System.Text.Json.Serialization;
using FirecrawlApi.Models.Enums;

namespace FirecrawlApi.Models;

public record Markdown
{
    [JsonPropertyName("type")]
    public required Type1 Type { get; init; }
}
