using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace FirecrawlApi.Models;

/// <summary>
/// Menu information extracted from the page if <c>menu</c> is in <c>formats</c>. Includes the merchant, currency, and a list of sections, where each section carries items with description, images, price, availability, dietary tags, calories, and option groups.
/// </summary>
public record Menu1
{
    /// <summary>
    /// Whether the page was identified as a menu.
    /// </summary>
    [JsonPropertyName("isMenu")]
    public required bool IsMenu { get; init; }

    /// <summary>
    /// A confidence score between 0 and 1 for the menu extraction.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("confidence")]
    public double? Confidence { get; init; }

    /// <summary>
    /// The merchant the menu belongs to.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("merchant")]
    public Merchant? Merchant { get; init; }

    /// <summary>
    /// The ISO 4217 currency code for the menu (e.g. 'USD'), reported only when the page sources it.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("currency")]
    public string? Currency { get; init; }

    /// <summary>
    /// Menu sections (e.g. 'Appetizers', 'Entrees').
    /// </summary>
    [JsonPropertyName("sections")]
    public required IReadOnlyList<Section> Sections { get; init; }

    /// <summary>
    /// The URL the menu was extracted from.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("sourceUrl")]
    public string? SourceUrl { get; init; }
}
