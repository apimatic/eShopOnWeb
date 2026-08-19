using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace FirecrawlApi.Models;

/// <summary>
/// Results of the actions specified in the <c>actions</c> parameter. Only present if the <c>actions</c> parameter was provided in the request
/// </summary>
public record Actions
{
    /// <summary>
    /// Screenshot URLs, in the same order as the screenshot actions provided.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("screenshots")]
    public IReadOnlyList<string>? Screenshots { get; init; }

    /// <summary>
    /// Scrape contents, in the same order as the scrape actions provided.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("scrapes")]
    public IReadOnlyList<Scrape1>? Scrapes { get; init; }

    /// <summary>
    /// JavaScript return values, in the same order as the executeJavascript actions provided.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("javascriptReturns")]
    public IReadOnlyList<JavascriptReturn>? JavascriptReturns { get; init; }

    /// <summary>
    /// PDFs generated, in the same order as the pdf actions provided.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("pdfs")]
    public IReadOnlyList<string>? Pdfs { get; init; }
}
