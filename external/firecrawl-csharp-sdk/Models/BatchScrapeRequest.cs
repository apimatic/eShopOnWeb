using System.Collections.Generic;
using System.Text.Json.Serialization;
using FirecrawlApi.Core.Validation.Attributes;
using FirecrawlApi.Models.AnyOf;
using FirecrawlApi.Models.Enums;

namespace FirecrawlApi.Models;

public record BatchScrapeRequest
{
    [JsonPropertyName("urls")]
    public required IReadOnlyList<string> Urls { get; init; }

    /// <summary>
    /// A webhook specification object.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("webhook")]
    public Webhook? Webhook { get; init; }

    /// <summary>
    /// Maximum number of concurrent scrapes. This parameter allows you to set a concurrency limit for this batch scrape. If not specified, the batch scrape adheres to your team's concurrency limit.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("maxConcurrency")]
    public int? MaxConcurrency { get; init; }

    /// <summary>
    /// If invalid URLs are specified in the urls array, they will be ignored. Instead of them failing the entire request, a batch scrape using the remaining valid URLs will be created, and the invalid URLs will be returned in the invalidURLs field of the response.
    /// </summary>
    [JsonPropertyName("ignoreInvalidURLs")]
    public bool? IgnoreInvalidUrLs { get; init; } = true;

    /// <summary>
    /// Output formats to include in the response. You can specify one or more formats, either as strings (e.g., <c>'markdown'</c>) or as objects with additional options (e.g., <c>{ type: 'json', schema: {...} }</c>). Some formats require specific options to be set. Example: <c>['markdown', { type: 'json', schema: {...} }]</c>.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("formats")]
    public IReadOnlyList<Format>? Formats { get; init; }

    /// <summary>
    /// Only return the main content of the page excluding headers, navs, footers, etc. This is a deterministic HTML-level filter applied before markdown is generated; no LLM is involved.
    /// </summary>
    [JsonPropertyName("onlyMainContent")]
    public bool? OnlyMainContent { get; init; } = true;

    /// <summary>
    /// Beta. Run an additional LLM-based pass over the generated markdown to remove residual boilerplate that <c>onlyMainContent</c> can miss (cookie banners, ad blocks, social share widgets, breadcrumbs, newsletter signups, comment sections, related-article lists). Headings, lists, tables, code blocks, image references, and inline links are preserved. Can be combined with <c>onlyMainContent</c> (the most common setup) or used on its own. Skipped with a warning when the markdown exceeds the cleaning model's output token limit (the original markdown is preserved). Not supported on zero-data-retention requests.
    /// </summary>
    [JsonPropertyName("onlyCleanContent")]
    public bool? OnlyCleanContent { get; init; } = false;

    /// <summary>
    /// Tags to include in the output.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("includeTags")]
    public IReadOnlyList<string>? IncludeTags { get; init; }

    /// <summary>
    /// Tags to exclude from the output.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("excludeTags")]
    public IReadOnlyList<string>? ExcludeTags { get; init; }

    /// <summary>
    /// Returns a cached version of the page if it is younger than this age in milliseconds. If a cached version of the page is older than this value, the page will be scraped. If you do not need extremely fresh data, enabling this can speed up your scrapes by 500%. Defaults to 2 days.
    /// </summary>
    [JsonPropertyName("maxAge")]
    public int? MaxAge { get; init; } = 172800000;

    /// <summary>
    /// When set, the request only checks the cache and never triggers a fresh scrape. The value is in milliseconds and specifies the minimum age the cached data must be. If matching cached data exists, it is returned instantly. If no cached data is found, a 404 with error code SCRAPE_NO_CACHED_DATA is returned. Set to 1 to accept any cached data regardless of age.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("minAge")]
    public int? MinAge { get; init; }

    /// <summary>
    /// Headers to send with the request. Can be used to send cookies, user-agent, etc.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("headers")]
    public object? Headers { get; init; }

    /// <summary>
    /// Specify a delay in milliseconds before fetching the content, allowing the page sufficient time to load. This waiting time is in addition to Firecrawl's smart wait feature.
    /// </summary>
    [JsonPropertyName("waitFor")]
    public int? WaitFor { get; init; } = 0;

    /// <summary>
    /// Set to true if you want to emulate scraping from a mobile device. Useful for testing responsive pages and taking mobile screenshots.
    /// </summary>
    [JsonPropertyName("mobile")]
    public bool? Mobile { get; init; } = false;

    /// <summary>
    /// Skip TLS certificate verification when making requests.
    /// </summary>
    [JsonPropertyName("skipTlsVerification")]
    public bool? SkipTlsVerification { get; init; } = true;

    /// <summary>
    /// Timeout in milliseconds for the request. Minimum is 1000 (1 second). Default is 60000 (60 seconds). Maximum is 300000 (300 seconds).
    /// </summary>
    [JsonPropertyName("timeout")]
    [Minimum(1000)]
    [Maximum(300000)]
    public int? Timeout { get; init; } = 60000;

    /// <summary>
    /// Controls how files are processed during scraping. When "pdf" is included (default), the PDF content is extracted and converted to markdown format, with billing based on the number of pages (1 credit per page). When an empty array is passed, the PDF file is returned in base64 encoding with a flat rate of 1 credit for the entire PDF.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("parsers")]
    public IReadOnlyList<Parser>? Parsers { get; init; }

    /// <summary>
    /// Actions to perform on the page before grabbing the content
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("actions")]
    public IReadOnlyList<ActionModel>? Actions { get; init; }

    /// <summary>
    /// Location settings for the request. When specified, this will use an appropriate proxy if available and emulate the corresponding language and timezone settings. Defaults to 'US' if not specified.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("location")]
    public Location? Location { get; init; }

    /// <summary>
    /// Removes all base 64 images from the markdown output, which may be overwhelmingly long. This does not affect html or rawHtml formats. The image's alt text remains in the output, but the URL is replaced with a placeholder.
    /// </summary>
    [JsonPropertyName("removeBase64Images")]
    public bool? RemoveBase64Images { get; init; } = true;

    /// <summary>
    /// Enables ad-blocking and cookie popup blocking.
    /// </summary>
    [JsonPropertyName("blockAds")]
    public bool? BlockAds { get; init; } = true;

    /// <summary>
    /// Specifies the type of proxy to use.
    /// <list type="bullet">
    ///   <item><description><b>basic</b>: Proxies for scraping sites with none to basic anti-bot solutions. Fast and usually works.</description></item>
    ///   <item><description><b>enhanced</b>: Enhanced proxies for scraping sites with advanced anti-bot solutions. Slower, but more reliable on certain sites. Billed at the same credit cost as basic.</description></item>
    ///   <item><description><b>auto</b>: Firecrawl will automatically retry scraping with enhanced proxies if the basic proxy fails. Enhanced proxies carry no credit surcharge, so either way only the regular cost is billed.</description></item>
    /// </list>
    /// </summary>
    [JsonPropertyName("proxy")]
    public Proxy? Proxy { get; init; } = Proxy.Auto;

    /// <summary>
    /// If true, the page will be stored in the Firecrawl index and cache. Setting this to false is useful if your scraping activity may have data protection concerns. Using some parameters associated with sensitive scraping (e.g. actions, headers) will force this parameter to be false.
    /// </summary>
    [JsonPropertyName("storeInCache")]
    public bool? StoreInCache { get; init; } = true;

    /// <summary>
    /// If true, serves the request from Firecrawl's cache only and never makes an outbound request to the target URL. Designed for compliance-constrained or air-gapped environments where the scrape request itself could leak sensitive information. On cache miss, returns a 404 with error code SCRAPE_LOCKDOWN_CACHE_MISS (the URL is never logged on miss). Lockdown requests are treated as zero data retention. Default maxAge is extended to 2 years so existing cached pages remain eligible. Billed at 5 credits on hit, 1 credit on cache miss.
    /// </summary>
    [JsonPropertyName("lockdown")]
    public bool? Lockdown { get; init; } = false;

    /// <summary>
    /// Redact personally identifiable information from returned markdown. Pass <c>true</c> to use defaults, or an object to tune mode, entities, and replacement style.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("redactPII")]
    public RedactPii? RedactPii { get; init; }

    /// <summary>
    /// Enable persistent browser storage across scrape and interact sessions. Pass a profile when scraping to preserve cookies, localStorage, and session data. Sessions with the same profile name share browser state.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("profile")]
    public Profile? Profile { get; init; }

    /// <summary>
    /// Per-request <see href="https://docs.firecrawl.dev/features/threat-protection">Threat Protection</see> override. Fields you provide replace the corresponding fields of your organization's policy for this request only; omitted fields keep their organization-level values. Requires Threat Protection to be enabled for your team (enterprise feature) — otherwise the request is rejected with a 403. If your organization has disabled request overrides, any request that includes this object is rejected with a 403. If Threat Protection is enforced for your team, <c>mode</c> may not be set to <c>off</c>.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("threatProtection")]
    public ThreatProtectionOverride? ThreatProtection { get; init; }

    /// <summary>
    /// User attribution included with SIEM logging events when SIEM Logging is enabled for the organization.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("auditMetadata")]
    public AuditMetadata? AuditMetadata { get; init; }

    /// <summary>
    /// If true, this will enable zero data retention for this batch scrape. To enable this feature, please contact help@firecrawl.dev
    /// </summary>
    [JsonPropertyName("zeroDataRetention")]
    public bool? ZeroDataRetention { get; init; } = false;
}
