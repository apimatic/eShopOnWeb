using System.Text.Json.Serialization;
using FirecrawlApi.Models.Enums;

namespace FirecrawlApi.Models;

public record Summary
{
    [JsonPropertyName("type")]
    public required Type2 Type { get; init; }
}
