using System.Collections.Generic;
using System.Text.Json.Serialization;
using FirecrawlApi.Core.Validation;
using FirecrawlApi.Core.Validation.Attributes;
using FirecrawlApi.Models.AnyOf;
using FirecrawlApi.Models.Enums;

namespace FirecrawlApi.Models;

public record Metadata
{
    /// <summary>
    /// Title extracted from the page, can be a string or array of strings
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("title")]
    public Title? Title { get; init; }

    /// <summary>
    /// Description extracted from the page, can be a string or array of strings
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("description")]
    public Description? Description { get; init; }

    /// <summary>
    /// Language extracted from the page, can be a string or array of strings
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("language")]
    public Language? Language { get; init; }

    /// <summary>
    /// The original URL that was requested. May differ from the page's final URL if redirects occurred.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("sourceURL")]
    [Format(FormatKind.Uri)]
    public string? SourceUrl { get; init; }

    /// <summary>
    /// The final URL of the page after all redirects have been followed.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("url")]
    [Format(FormatKind.Uri)]
    public string? Url { get; init; }

    /// <summary>
    /// Keywords extracted from the page, can be a string or array of strings
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("keywords")]
    public Keywords? Keywords { get; init; }

    /// <summary>
    /// Alternative locales for the page
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("ogLocaleAlternate")]
    public IReadOnlyList<string>? OgLocaleAlternate { get; init; }

    /// <summary>
    /// Other metadata extracted from HTML, can be a string or array of strings
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("<any other metadata>")]
    public AnyOtherMetadata? AnyOtherMetadata { get; init; }

    /// <summary>
    /// The status code of the page
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("statusCode")]
    public int? StatusCode { get; init; }

    /// <summary>
    /// For PDF inputs, the number of pages parsed (capped by the parsers maxPages option).
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("numPages")]
    public int? NumPages { get; init; }

    /// <summary>
    /// For PDF inputs, the document's true page count before any maxPages capping. Omitted when it cannot be determined; a totalPages greater than numPages indicates the result was truncated.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("totalPages")]
    public int? TotalPages { get; init; }

    /// <summary>
    /// The content type (MIME type) of the page, e.g. text/html, application/pdf
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("contentType")]
    public string? ContentType { get; init; }

    /// <summary>
    /// The error message of the page
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("error")]
    public string? Error { get; init; }

    /// <summary>
    /// Whether this scrape was throttled due to team concurrency limits
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("concurrencyLimited")]
    public bool? ConcurrencyLimited { get; init; }

    /// <summary>
    /// Time in milliseconds the request waited in the concurrency queue. Only present when concurrencyLimited is true.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("concurrencyQueueDurationMs")]
    public double? ConcurrencyQueueDurationMs { get; init; }
}
