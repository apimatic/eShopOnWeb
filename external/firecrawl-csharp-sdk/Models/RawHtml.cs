using System.Text.Json.Serialization;
using FirecrawlApi.Models.Enums;

namespace FirecrawlApi.Models;

public record RawHtml
{
    [JsonPropertyName("type")]
    public required Type4 Type { get; init; }
}
