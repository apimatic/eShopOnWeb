using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace FirecrawlApi.Models;

public record Data1
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("markdown")]
    public string? Markdown { get; init; }

    /// <summary>
    /// Summary of the page if <c>summary</c> is in <c>formats</c>
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("summary")]
    public string? Summary { get; init; }

    /// <summary>
    /// Cleaned HTML of the page if <c>html</c> is in <c>formats</c>. Removes <c>&lt;script&gt;</c>, <c>&lt;style&gt;</c>, <c>&lt;noscript&gt;</c>, <c>&lt;meta&gt;</c>, and <c>&lt;head&gt;</c> tags; converts relative URLs to absolute; resolves responsive image <c>srcset</c> to the largest version. Respects <c>onlyMainContent</c>, <c>includeTags</c>, and <c>excludeTags</c> filters.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("html")]
    public string? Html { get; init; }

    /// <summary>
    /// The exact, unmodified HTML as received from the page if <c>rawHtml</c> is in <c>formats</c>. No cleaning or filtering is applied.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("rawHtml")]
    public string? RawHtml { get; init; }

    /// <summary>
    /// Screenshot of the page if <c>screenshot</c> is in <c>formats</c>. Screenshots expire after 24 hours and can no longer be downloaded.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("screenshot")]
    public string? Screenshot { get; init; }

    /// <summary>
    /// Signed URL to the extracted MP3 audio file if <c>audio</c> is in <c>formats</c>. The signed URL expires after 1 hour.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("audio")]
    public string? Audio { get; init; }

    /// <summary>
    /// Signed URL to the extracted video file if <c>video</c> is in <c>formats</c>. The signed URL expires after 1 hour.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("video")]
    public string? Video { get; init; }

    /// <summary>
    /// Natural-language answer to the question supplied via the <c>question</c> format. Only present if a <c>question</c> format object was included in <c>formats</c>.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("answer")]
    public string? Answer { get; init; }

    /// <summary>
    /// Relevant source text selected by the <c>highlights</c> format. Only present if a <c>highlights</c> format object was included in <c>formats</c>.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("highlights")]
    public string? Highlights { get; init; }

    /// <summary>
    /// List of links on the page if <c>links</c> is in <c>formats</c>
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("links")]
    public IReadOnlyList<string>? Links { get; init; }

    /// <summary>
    /// Results of the actions specified in the <c>actions</c> parameter. Only present if the <c>actions</c> parameter was provided in the request
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("actions")]
    public Actions? Actions { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("metadata")]
    public Metadata? Metadata { get; init; }

    /// <summary>
    /// Can be displayed when using LLM Extraction. Warning message will let you know any issues with the extraction.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("warning")]
    public string? Warning { get; init; }

    /// <summary>
    /// Change tracking information if <c>changeTracking</c> is in <c>formats</c>. Only present when the <c>changeTracking</c> format is requested.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("changeTracking")]
    public ChangeTracking1? ChangeTracking { get; init; }

    /// <summary>
    /// Branding information extracted from the page if <c>branding</c> is in <c>formats</c>. Includes colors, fonts, typography, spacing, components, and more.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("branding")]
    public Branding1? Branding { get; init; }

    /// <summary>
    /// Product information extracted from the page if <c>product</c> is in <c>formats</c>. Includes title, brand, category, description, and variants. Pricing, availability, and images live on each variant.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("product")]
    public Product1? Product { get; init; }

    /// <summary>
    /// Menu information extracted from the page if <c>menu</c> is in <c>formats</c>. Includes the merchant, currency, and a list of sections, where each section carries items with description, images, price, availability, dietary tags, calories, and option groups.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("menu")]
    public Menu1? Menu { get; init; }
}
