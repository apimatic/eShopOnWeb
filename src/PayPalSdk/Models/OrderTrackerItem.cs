using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using PayPal.Core.Models;
using PayPal.Core.Validation;
using PayPal.Core.Validation.Attributes;

namespace PayPal.Models;

/// <summary>
/// The details of the items in the shipment.
/// </summary>
public record OrderTrackerItem
{
    /// <summary>
    /// The item name or title.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("name")]
    [StringLength(127, MinimumLength = 1)]
    public string? Name { get; init; }

    /// <summary>
    /// The item quantity. Must be a whole number.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("quantity")]
    [StringLength(10, MinimumLength = 1)]
    [RegularExpression("^[1-9][0-9]{0,9}$")]
    public string? Quantity { get; init; }

    /// <summary>
    /// The stock keeping unit (SKU) for the item. This can contain unicode characters.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("sku")]
    [StringLength(127, MinimumLength = 1)]
    public string? Sku { get; init; }

    /// <summary>
    /// The URL to the item being purchased. Visible to buyer and used in buyer experiences.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("url")]
    [StringLength(2048, MinimumLength = 1)]
    [Format(FormatKind.Uri)]
    public string? Url { get; init; }

    /// <summary>
    /// The URL of the item's image. File type and size restrictions apply. An image that violates these restrictions will not be honored.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("image_url")]
    [StringLength(2048, MinimumLength = 1)]
    [RegularExpression("^(https:)([/|.|\\w|\\s|-])*\\.(?:jpg|gif|png|jpeg|JPG|GIF|PNG|JPEG)")]
    [Format(FormatKind.Uri)]
    public string? ImageUrl { get; init; }

    /// <summary>
    /// The Universal Product Code of the item.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("upc")]
    public UniversalProductCode? Upc { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
