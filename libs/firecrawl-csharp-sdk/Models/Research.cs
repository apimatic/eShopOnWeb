using System.Text.Json.Serialization;
using FirecrawlApi.Models.Enums;

namespace FirecrawlApi.Models;

public record Research
{
    [JsonPropertyName("type")]
    public required Type44 Type { get; init; }
}
