using System;
using System.Text.Json.Serialization;
using FirecrawlApi.Core.Validation;
using FirecrawlApi.Core.Validation.Attributes;

namespace FirecrawlApi.Models;

public record Crawl
{
    /// <summary>
    /// The unique identifier of the crawl
    /// </summary>
    [JsonPropertyName("id")]
    public required Guid Id { get; init; }

    /// <summary>
    /// The ID of the team that owns the crawl
    /// </summary>
    [JsonPropertyName("teamId")]
    public required string TeamId { get; init; }

    /// <summary>
    /// The origin URL of the crawl
    /// </summary>
    [JsonPropertyName("url")]
    [Format(FormatKind.Uri)]
    public required string Url { get; init; }

    /// <summary>
    /// The crawler options used for this crawl
    /// </summary>
    [JsonPropertyName("options")]
    public required Options Options { get; init; }
}
