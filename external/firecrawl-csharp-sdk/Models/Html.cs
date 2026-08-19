using System.Text.Json.Serialization;
using FirecrawlApi.Models.Enums;

namespace FirecrawlApi.Models;

public record Html
{
    [JsonPropertyName("type")]
    public required Type3 Type { get; init; }
}
