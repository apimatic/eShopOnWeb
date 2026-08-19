using System.Text.Json.Serialization;
using FirecrawlApi.Models.Enums;

namespace FirecrawlApi.Models;

public record Branding
{
    [JsonPropertyName("type")]
    public required Type10 Type { get; init; }
}
