using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace FirecrawlApi.Models;

/// <summary>
/// Product information extracted from the page if <c>product</c> is in <c>formats</c>. Includes title, brand, category, description, and variants. Pricing, availability, and images live on each variant.
/// </summary>
public record Product1
{
    /// <summary>
    /// The product title.
    /// </summary>
    [JsonPropertyName("title")]
    public required string Title { get; init; }

    /// <summary>
    /// The product brand or manufacturer.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("brand")]
    public string? Brand { get; init; }

    /// <summary>
    /// The product category, optionally as a breadcrumb path (e.g. 'Electronics &gt; Audio &gt; Headphones').
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("category")]
    public string? Category { get; init; }

    /// <summary>
    /// The canonical URL of the product page.
    /// </summary>
    [JsonPropertyName("url")]
    public required string Url { get; init; }

    /// <summary>
    /// The product description.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("description")]
    public string? Description { get; init; }

    /// <summary>
    /// Product variants (e.g. different colors or sizes).
    /// </summary>
    [JsonPropertyName("variants")]
    public required IReadOnlyList<Variant> Variants { get; init; }
}
