using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using FirecrawlApi.Core.Validation.Attributes;
using FirecrawlApi.Models.AnyOf;
using FirecrawlApi.Models.Enums;

namespace FirecrawlApi.Models;

public record SearchRequest
{
    /// <summary>
    /// The search query
    /// </summary>
    [JsonPropertyName("query")]
    [MaxLength(500)]
    public required string Query { get; init; }

    /// <summary>
    /// Maximum number of results to return (per source type when using multiple sources)
    /// </summary>
    [JsonPropertyName("limit")]
    [Minimum(1)]
    [Maximum(100)]
    public int? Limit { get; init; } = 10;

    /// <summary>
    /// Sources to search. Will determine the arrays available in the response. Defaults to ['web'].
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("sources")]
    public IReadOnlyList<Source1>? Sources { get; init; }

    /// <summary>
    /// Categories to filter results by. Defaults to [], which means results will not be filtered by any categories.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("categories")]
    public IReadOnlyList<Category>? Categories { get; init; }

    /// <summary>
    /// Restricts search results to the specified domains. Domains should be hostnames only, without protocol or path. Cannot be used with excludeDomains.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("includeDomains")]
    public IReadOnlyList<string>? IncludeDomains { get; init; }

    /// <summary>
    /// Excludes search results from the specified domains. Domains should be hostnames only, without protocol or path. Cannot be used with includeDomains.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("excludeDomains")]
    public IReadOnlyList<string>? ExcludeDomains { get; init; }

    /// <summary>
    /// Time-based search parameter. Supports predefined time ranges (<c>qdr:h</c>, <c>qdr:d</c>, <c>qdr:w</c>, <c>qdr:m</c>, <c>qdr:y</c>), custom date ranges (<c>cdr:1,cd_min:MM/DD/YYYY,cd_max:MM/DD/YYYY</c>), and sort by date (<c>sbd:1</c>). Values can be combined, e.g. <c>sbd:1,qdr:w</c>.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("tbs")]
    public string? Tbs { get; init; }

    /// <summary>
    /// Location parameter for search results (e.g. <c>San Francisco,California,United States</c>). For best results, set both this and the <c>country</c> parameter.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("location")]
    public string? Location { get; init; }

    /// <summary>
    /// ISO country code for geo-targeting search results (e.g. <c>US</c>). For best results, set both this and the <c>location</c> parameter.
    /// </summary>
    [JsonPropertyName("country")]
    public string? Country { get; init; } = "US";

    /// <summary>
    /// When <c>true</c>, filters explicit content from search results (SafeSearch). Omit to keep the default behavior, which does not apply the filter.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("safe")]
    public bool? Safe { get; init; }

    /// <summary>
    /// Timeout in milliseconds
    /// </summary>
    [JsonPropertyName("timeout")]
    public int? Timeout { get; init; } = 60000;

    /// <summary>
    /// Excludes URLs from the search results that are invalid for other Firecrawl endpoints. This helps reduce errors if you are piping data from search into other Firecrawl API endpoints.
    /// </summary>
    [JsonPropertyName("ignoreInvalidURLs")]
    public bool? IgnoreInvalidUrLs { get; init; } = false;

    /// <summary>
    /// Generate query-relevant highlights for search results. Set to false to return provider descriptions or snippets without highlighting.
    /// </summary>
    [JsonPropertyName("highlights")]
    public bool? Highlights { get; init; } = true;

    /// <summary>
    /// Enterprise search options for Zero Data Retention (ZDR). Use <c>["zdr"]</c> for end-to-end ZDR (10 credits / 10 results) or <c>["anon"]</c> for anonymized ZDR (2 credits / 10 results). Must be enabled for your team.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("enterprise")]
    public IReadOnlyList<Enterprise>? Enterprise { get; init; }

    /// <summary>
    /// Options for scraping search results
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("scrapeOptions")]
    public ScrapeOptions? ScrapeOptions { get; init; }

    /// <summary>
    /// Per-request <see href="https://docs.firecrawl.dev/features/threat-protection">Threat Protection</see> override. Fields you provide replace the corresponding fields of your organization's policy for this request only; omitted fields keep their organization-level values. Requires Threat Protection to be enabled for your team (enterprise feature) — otherwise the request is rejected with a 403. If your organization has disabled request overrides, any request that includes this object is rejected with a 403. If Threat Protection is enforced for your team, <c>mode</c> may not be set to <c>off</c>.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("threatProtection")]
    public ThreatProtectionOverride? ThreatProtection { get; init; }
}
