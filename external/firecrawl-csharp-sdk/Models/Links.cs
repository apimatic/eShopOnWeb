using System.Text.Json.Serialization;
using FirecrawlApi.Models.Enums;

namespace FirecrawlApi.Models;

public record Links
{
    [JsonPropertyName("type")]
    public required Type5 Type { get; init; }
}
