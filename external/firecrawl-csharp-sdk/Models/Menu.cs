using System.Text.Json.Serialization;
using FirecrawlApi.Models.Enums;

namespace FirecrawlApi.Models;

public record Menu
{
    [JsonPropertyName("type")]
    public required Type12 Type { get; init; }
}
