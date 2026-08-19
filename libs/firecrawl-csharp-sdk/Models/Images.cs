using System.Text.Json.Serialization;
using FirecrawlApi.Models.Enums;

namespace FirecrawlApi.Models;

public record Images
{
    [JsonPropertyName("type")]
    public required Type6 Type { get; init; }
}
