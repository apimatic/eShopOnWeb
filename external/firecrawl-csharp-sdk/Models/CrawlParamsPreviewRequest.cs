using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using FirecrawlApi.Core.Validation;
using FirecrawlApi.Core.Validation.Attributes;

namespace FirecrawlApi.Models;

public record CrawlParamsPreviewRequest
{
    /// <summary>
    /// The URL to crawl
    /// </summary>
    [JsonPropertyName("url")]
    [Format(FormatKind.Uri)]
    public required string Url { get; init; }

    /// <summary>
    /// Natural language prompt describing what you want to crawl
    /// </summary>
    [JsonPropertyName("prompt")]
    [MaxLength(10000)]
    public required string Prompt { get; init; }
}
