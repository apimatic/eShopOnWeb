using System.Text.Json.Serialization;
using FirecrawlApi.Models.Enums;

namespace FirecrawlApi.Models;

public record News
{
    [JsonPropertyName("type")]
    public required Type42 Type { get; init; }
}
