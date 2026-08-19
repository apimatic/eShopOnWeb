using System.Text.Json.Serialization;
using FirecrawlApi.Models.Enums;

namespace FirecrawlApi.Models;

public record Pdf
{
    [JsonPropertyName("type")]
    public required Type17 Type { get; init; }
}
