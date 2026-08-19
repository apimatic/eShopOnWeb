using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace FirecrawlApi.Models;

public record ExtractRequest
{
    [JsonPropertyName("urls")]
    public required IReadOnlyList<string> Urls { get; init; }

    /// <summary>
    /// Prompt to guide the extraction process
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("prompt")]
    public string? Prompt { get; init; }

    /// <summary>
    /// Schema to define the structure of the extracted data. Must conform to <see href="https://json-schema.org/">JSON Schema</see>.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("schema")]
    public object? Schema { get; init; }

    /// <summary>
    /// When true, the extraction will use web search to find additional data
    /// </summary>
    [JsonPropertyName("enableWebSearch")]
    public bool? EnableWebSearch { get; init; } = false;

    /// <summary>
    /// When true, sitemap.xml files will be ignored during website scanning
    /// </summary>
    [JsonPropertyName("ignoreSitemap")]
    public bool? IgnoreSitemap { get; init; } = false;

    /// <summary>
    /// When true, subdomains of the provided URLs will also be scanned
    /// </summary>
    [JsonPropertyName("includeSubdomains")]
    public bool? IncludeSubdomains { get; init; } = true;

    /// <summary>
    /// When true, the sources used to extract the data will be included in the response as <c>sources</c> key
    /// </summary>
    [JsonPropertyName("showSources")]
    public bool? ShowSources { get; init; } = false;

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("scrapeOptions")]
    public ScrapeOptions? ScrapeOptions { get; init; }

    /// <summary>
    /// If invalid URLs are specified in the urls array, they will be ignored. Instead of them failing the entire request, an extract using the remaining valid URLs will be performed, and the invalid URLs will be returned in the invalidURLs field of the response.
    /// </summary>
    [JsonPropertyName("ignoreInvalidURLs")]
    public bool? IgnoreInvalidUrLs { get; init; } = true;

    /// <summary>
    /// Per-request <see href="https://docs.firecrawl.dev/features/threat-protection">Threat Protection</see> override. Fields you provide replace the corresponding fields of your organization's policy for this request only; omitted fields keep their organization-level values. Requires Threat Protection to be enabled for your team (enterprise feature) — otherwise the request is rejected with a 403. If your organization has disabled request overrides, any request that includes this object is rejected with a 403. If Threat Protection is enforced for your team, <c>mode</c> may not be set to <c>off</c>.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("threatProtection")]
    public ThreatProtectionOverride? ThreatProtection { get; init; }
}
