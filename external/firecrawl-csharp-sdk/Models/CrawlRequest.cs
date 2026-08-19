using System.Collections.Generic;
using System.Text.Json.Serialization;
using FirecrawlApi.Core.Validation;
using FirecrawlApi.Core.Validation.Attributes;
using FirecrawlApi.Models.Enums;

namespace FirecrawlApi.Models;

public record CrawlRequest
{
    /// <summary>
    /// The base URL to start crawling from
    /// </summary>
    [JsonPropertyName("url")]
    [Format(FormatKind.Uri)]
    public required string Url { get; init; }

    /// <summary>
    /// A prompt to use to generate the crawler options (all the parameters below) from natural language. Explicitly set parameters will override the generated equivalents.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("prompt")]
    public string? Prompt { get; init; }

    /// <summary>
    /// URL pathname regex patterns that exclude matching URLs from the crawl. For example, if you set "excludePaths": ["blog/.*"] for the base URL firecrawl.dev, any results matching that pattern will be excluded, such as https://www.firecrawl.dev/blog/firecrawl-launch-week-1-recap.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("excludePaths")]
    public IReadOnlyList<string>? ExcludePaths { get; init; }

    /// <summary>
    /// URL pathname regex patterns that include matching URLs in the crawl. Only the paths that match the specified patterns will be included in the response. Note: the starting URL is also checked against these patterns — if it does not match, the crawl may return 0 pages. For example, if you set "includePaths": ["blog/.*"] for the base URL firecrawl.dev/blog, only pages under /blog/ will be included in the results, such as https://www.firecrawl.dev/blog/firecrawl-launch-week-1-recap.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("includePaths")]
    public IReadOnlyList<string>? IncludePaths { get; init; }

    /// <summary>
    /// Maximum depth to crawl based on discovery order. The root site and sitemapped pages has a discovery depth of 0. For example, if you set it to 1, and you set <c>sitemap: 'skip'</c>, you will only crawl the entered URL and all URLs that are linked on that page.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("maxDiscoveryDepth")]
    public int? MaxDiscoveryDepth { get; init; }

    /// <summary>
    /// Sitemap mode when crawling. If you set it to 'skip', the crawler will ignore the website sitemap and only crawl the entered URL and discover pages from there onwards. If you set it to 'only', the crawler will only crawl URLs from the sitemap (plus the start URL) and will not discover links from HTML.
    /// </summary>
    [JsonPropertyName("sitemap")]
    public Sitemap? Sitemap { get; init; } = Sitemap.Include;

    /// <summary>
    /// Do not re-scrape the same path with different (or none) query parameters
    /// </summary>
    [JsonPropertyName("ignoreQueryParameters")]
    public bool? IgnoreQueryParameters { get; init; } = false;

    /// <summary>
    /// When true, includePaths and excludePaths regex patterns are matched against the full URL (including query parameters) instead of just the URL pathname. Useful when you need to filter URLs based on query strings.
    /// </summary>
    [JsonPropertyName("regexOnFullURL")]
    public bool? RegexOnFullUrl { get; init; } = false;

    /// <summary>
    /// Maximum number of pages to crawl. Default limit is 10000.
    /// </summary>
    [JsonPropertyName("limit")]
    public int? Limit { get; init; } = 10000;

    /// <summary>
    /// Allows the crawler to follow internal links to sibling or parent URLs, not just child paths.
    /// <para>
    /// false: Only crawls deeper (child) URLs.
    /// → e.g. /features/feature-1 → /features/feature-1/tips ✅
    /// → Won't follow /pricing or / ❌
    /// </para>
    /// <para>
    /// true: Crawls any internal links, including siblings and parents.
    /// → e.g. /features/feature-1 → /pricing, /, etc. ✅
    /// </para>
    /// <para>
    /// Use true for broader internal coverage beyond nested paths.
    /// </para>
    /// </summary>
    [JsonPropertyName("crawlEntireDomain")]
    public bool? CrawlEntireDomain { get; init; } = false;

    /// <summary>
    /// Allows the crawler to follow links to external websites. External links are followed one hop (the links found on those external pages are not crawled). Links pointing to an external site's homepage (a root URL with no path) are skipped and reported in Get Crawl Errors with the code EXTERNAL_LINK; redirects to an external homepage are skipped for the same reason.
    /// </summary>
    [JsonPropertyName("allowExternalLinks")]
    public bool? AllowExternalLinks { get; init; } = false;

    /// <summary>
    /// Allows the crawler to follow links to subdomains of the main domain.
    /// </summary>
    [JsonPropertyName("allowSubdomains")]
    public bool? AllowSubdomains { get; init; } = false;

    /// <summary>
    /// Ignore the website's robots.txt rules. Enterprise only — contact support@firecrawl.com to enable.
    /// </summary>
    [JsonPropertyName("ignoreRobotsTxt")]
    public bool? IgnoreRobotsTxt { get; init; } = false;

    /// <summary>
    /// Custom User-Agent string for robots.txt evaluation. When set, robots.txt is fetched with this User-Agent and allow/disallow rules are matched against it instead of the default. Enterprise only — contact support@firecrawl.com to enable.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("robotsUserAgent")]
    public string? RobotsUserAgent { get; init; }

    /// <summary>
    /// Delay in seconds between scrapes. This helps respect website rate limits. Setting this forces concurrency to 1.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("delay")]
    public double? Delay { get; init; }

    /// <summary>
    /// Maximum number of concurrent scrapes. This parameter allows you to set a concurrency limit for this crawl. If not specified, the crawl adheres to your team's concurrency limit.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("maxConcurrency")]
    public int? MaxConcurrency { get; init; }

    /// <summary>
    /// A webhook specification object.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("webhook")]
    public Webhook1? Webhook { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("scrapeOptions")]
    public ScrapeOptions? ScrapeOptions { get; init; }

    /// <summary>
    /// If true, this will enable zero data retention for this crawl. To enable this feature, please contact help@firecrawl.dev
    /// </summary>
    [JsonPropertyName("zeroDataRetention")]
    public bool? ZeroDataRetention { get; init; } = false;
}
