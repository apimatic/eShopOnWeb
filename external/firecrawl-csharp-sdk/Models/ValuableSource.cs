using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using FirecrawlApi.Core.Validation;
using FirecrawlApi.Core.Validation.Attributes;

namespace FirecrawlApi.Models;

public record ValuableSource
{
    [JsonPropertyName("url")]
    [Format(FormatKind.Uri)]
    public required string Url { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("reason")]
    [MaxLength(1000)]
    public string? Reason { get; init; }
}
