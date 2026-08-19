using System.Text.Json.Serialization;
using FirecrawlApi.Models.Enums;

namespace FirecrawlApi.Models;

public record GitHub
{
    [JsonPropertyName("type")]
    public required Type43 Type { get; init; }
}
