using System.Collections.Generic;
using System.Text.Json.Serialization;
using FirecrawlApi.Core.Validation;
using FirecrawlApi.Core.Validation.Attributes;
using FirecrawlApi.Models.Enums;

namespace FirecrawlApi.Models;

public record Data4
{
    /// <summary>
    /// The URL to crawl
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("url")]
    [Format(FormatKind.Uri)]
    public string? Url { get; init; }

    /// <summary>
    /// URL patterns to include
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("includePaths")]
    public IReadOnlyList<string>? IncludePaths { get; init; }

    /// <summary>
    /// URL patterns to exclude
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("excludePaths")]
    public IReadOnlyList<string>? ExcludePaths { get; init; }

    /// <summary>
    /// Maximum crawl depth
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("maxDepth")]
    public int? MaxDepth { get; init; }

    /// <summary>
    /// Maximum discovery depth
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("maxDiscoveryDepth")]
    public int? MaxDiscoveryDepth { get; init; }

    /// <summary>
    /// Whether to crawl the entire domain
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("crawlEntireDomain")]
    public bool? CrawlEntireDomain { get; init; }

    /// <summary>
    /// Whether to allow external links
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("allowExternalLinks")]
    public bool? AllowExternalLinks { get; init; }

    /// <summary>
    /// Whether to allow subdomains
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("allowSubdomains")]
    public bool? AllowSubdomains { get; init; }

    /// <summary>
    /// Sitemap handling strategy
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("sitemap")]
    public Sitemap1? Sitemap { get; init; }

    /// <summary>
    /// Whether to ignore query parameters
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("ignoreQueryParameters")]
    public bool? IgnoreQueryParameters { get; init; }

    /// <summary>
    /// Whether robots.txt rules are ignored
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("ignoreRobotsTxt")]
    public bool? IgnoreRobotsTxt { get; init; }

    /// <summary>
    /// Custom User-Agent string used for robots.txt evaluation
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("robotsUserAgent")]
    public string? RobotsUserAgent { get; init; }

    /// <summary>
    /// Whether to deduplicate similar URLs
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("deduplicateSimilarURLs")]
    public bool? DeduplicateSimilarUrLs { get; init; }

    /// <summary>
    /// Delay between requests in milliseconds
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("delay")]
    public double? Delay { get; init; }

    /// <summary>
    /// Maximum number of pages to crawl
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("limit")]
    public int? Limit { get; init; }
}
