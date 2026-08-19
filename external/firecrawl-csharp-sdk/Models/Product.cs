using System.Text.Json.Serialization;
using FirecrawlApi.Models.Enums;

namespace FirecrawlApi.Models;

public record Product
{
    [JsonPropertyName("type")]
    public required Type11 Type { get; init; }
}
