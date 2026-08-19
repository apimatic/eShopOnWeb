using System.Text.Json.Serialization;
using FirecrawlApi.Core.Validation;
using FirecrawlApi.Core.Validation.Attributes;
using FirecrawlApi.Models.Enums;

namespace FirecrawlApi.Models;

public record MapRequest
{
    /// <summary>
    /// The base URL to start crawling from
    /// </summary>
    [JsonPropertyName("url")]
    [Format(FormatKind.Uri)]
    public required string Url { get; init; }

    /// <summary>
    /// Specify a search query to order the results by relevance. Example: 'blog' will return URLs that contain the word 'blog' in the URL ordered by relevance.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("search")]
    public string? Search { get; init; }

    /// <summary>
    /// Sitemap mode when mapping. If you set it to <c>skip</c>, the sitemap won't be used to find URLs. If you set it to <c>only</c>, only URLs that are in the sitemap will be returned. By default (<c>include</c>), the sitemap and other methods will be used together to find URLs.
    /// </summary>
    [JsonPropertyName("sitemap")]
    public Sitemap2? Sitemap { get; init; } = Sitemap2.Include;

    /// <summary>
    /// Include subdomains of the website
    /// </summary>
    [JsonPropertyName("includeSubdomains")]
    public bool? IncludeSubdomains { get; init; } = true;

    /// <summary>
    /// Do not return URLs with query parameters
    /// </summary>
    [JsonPropertyName("ignoreQueryParameters")]
    public bool? IgnoreQueryParameters { get; init; } = true;

    /// <summary>
    /// Bypass the sitemap cache to retrieve fresh URLs. Sitemap data is cached for up to 7 days; use this parameter when your sitemap has been recently updated.
    /// </summary>
    [JsonPropertyName("ignoreCache")]
    public bool? IgnoreCache { get; init; } = false;

    /// <summary>
    /// Maximum number of links to return
    /// </summary>
    [JsonPropertyName("limit")]
    [Maximum(100000)]
    public int? Limit { get; init; } = 5000;

    /// <summary>
    /// Timeout in milliseconds. There is no timeout by default.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("timeout")]
    public int? Timeout { get; init; }

    /// <summary>
    /// Location settings for the request. When specified, this will use an appropriate proxy if available and emulate the corresponding language and timezone settings. Defaults to 'US' if not specified.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("location")]
    public Location? Location { get; init; }

    /// <summary>
    /// User attribution included with SIEM logging events when SIEM Logging is enabled for the organization.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("auditMetadata")]
    public AuditMetadata? AuditMetadata { get; init; }

    /// <summary>
    /// Per-request <see href="https://docs.firecrawl.dev/features/threat-protection">Threat Protection</see> override. Fields you provide replace the corresponding fields of your organization's policy for this request only; omitted fields keep their organization-level values. Requires Threat Protection to be enabled for your team (enterprise feature) — otherwise the request is rejected with a 403. If your organization has disabled request overrides, any request that includes this object is rejected with a 403. If Threat Protection is enforced for your team, <c>mode</c> may not be set to <c>off</c>.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("threatProtection")]
    public ThreatProtectionOverride? ThreatProtection { get; init; }
}
