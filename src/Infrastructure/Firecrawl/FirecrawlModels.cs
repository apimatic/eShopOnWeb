using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.Infrastructure.Firecrawl;

// Request/response contracts hand-written against firecrawl-spec/openapi.json.
// Only the fields this integration uses are modelled; property names match the spec exactly.

/// <summary>Request body for <c>POST /crawl</c> (operationId: crawlUrls).</summary>
internal sealed class CrawlRequest
{
    [JsonPropertyName("url")]
    public string Url { get; set; } = string.Empty;

    [JsonPropertyName("limit")]
    public int? Limit { get; set; }

    [JsonPropertyName("crawlEntireDomain")]
    public bool? CrawlEntireDomain { get; set; }

    [JsonPropertyName("sitemap")]
    public string? Sitemap { get; set; }

    [JsonPropertyName("scrapeOptions")]
    public CrawlScrapeOptions? ScrapeOptions { get; set; }
}

/// <summary>Subset of the shared <c>ScrapeOptions</c> schema used per crawled page.</summary>
internal sealed class CrawlScrapeOptions
{
    [JsonPropertyName("onlyMainContent")]
    public bool? OnlyMainContent { get; set; }

    [JsonPropertyName("formats")]
    public List<object>? Formats { get; set; }
}

/// <summary>The <c>json</c> output format object (Formats -&gt; "JSON" variant in the spec).</summary>
internal sealed class JsonFormat
{
    [JsonPropertyName("type")]
    public string Type => "json";

    [JsonPropertyName("prompt")]
    public string? Prompt { get; set; }

    [JsonPropertyName("schema")]
    public object? Schema { get; set; }
}

/// <summary>Response body for <c>POST /crawl</c> (schema: CrawlResponse).</summary>
internal sealed class CrawlStartResponse
{
    [JsonPropertyName("success")]
    public bool Success { get; set; }

    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("url")]
    public string? Url { get; set; }
}

/// <summary>Response body for <c>GET /crawl/{id}</c> (schema: CrawlStatusResponseObj).</summary>
internal sealed class CrawlStatusResponse
{
    [JsonPropertyName("status")]
    public string? Status { get; set; }

    [JsonPropertyName("total")]
    public int Total { get; set; }

    [JsonPropertyName("completed")]
    public int Completed { get; set; }

    [JsonPropertyName("next")]
    public string? Next { get; set; }

    [JsonPropertyName("data")]
    public List<CrawlDataItem>? Data { get; set; }
}

/// <summary>One crawled page inside <c>CrawlStatusResponseObj.data</c>.</summary>
internal sealed class CrawlDataItem
{
    /// <summary>
    /// The structured data produced by the <c>json</c> format for this page. The spec declares
    /// the <c>json</c> format as an input; per Firecrawl's documented behavior the extracted
    /// object is returned on the page's <c>json</c> field.
    /// </summary>
    [JsonPropertyName("json")]
    public JsonElement? Json { get; set; }

    [JsonPropertyName("metadata")]
    public CrawlPageMetadata? Metadata { get; set; }
}

/// <summary>Subset of a crawled page's <c>metadata</c> object.</summary>
internal sealed class CrawlPageMetadata
{
    [JsonPropertyName("sourceURL")]
    public string? SourceUrl { get; set; }

    [JsonPropertyName("url")]
    public string? Url { get; set; }

    [JsonPropertyName("statusCode")]
    public int? StatusCode { get; set; }

    [JsonPropertyName("error")]
    public string? Error { get; set; }
}

/// <summary>Shape of the error responses declared throughout the spec (402/429/500/400).</summary>
internal sealed class FirecrawlErrorResponse
{
    [JsonPropertyName("error")]
    public string? Error { get; set; }

    [JsonPropertyName("code")]
    public string? Code { get; set; }

    [JsonPropertyName("success")]
    public bool? Success { get; set; }
}
