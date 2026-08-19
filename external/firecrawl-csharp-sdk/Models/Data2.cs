using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace FirecrawlApi.Models;

public record Data2
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("markdown")]
    public string? Markdown { get; init; }

    /// <summary>
    /// HTML version of the content on page if <c>includeHtml</c>  is true
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("html")]
    public string? Html { get; init; }

    /// <summary>
    /// Raw HTML content of the page if <c>includeRawHtml</c>  is true
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("rawHtml")]
    public string? RawHtml { get; init; }

    /// <summary>
    /// List of links on the page if <c>includeLinks</c> is true
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("links")]
    public IReadOnlyList<string>? Links { get; init; }

    /// <summary>
    /// Screenshot of the page if <c>includeScreenshot</c> is true
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("screenshot")]
    public string? Screenshot { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("metadata")]
    public Metadata1? Metadata { get; init; }
}
