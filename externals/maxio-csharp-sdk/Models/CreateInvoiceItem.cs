using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Maxio.Core.Models;
using Maxio.Models.AnyOf;

namespace Maxio.Models;

public record CreateInvoiceItem
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("title")]
    public string? Title { get; init; }

    /// <summary>
    /// The quantity can contain up to 8 decimal places. e.g., 1.00 or 0.0012 or 0.00000065. If you submit a value with more than 8 decimal places, we will round it down to the 8th decimal place.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("quantity")]
    public Quantity3? Quantity { get; init; }

    /// <summary>
    /// The unit_price can contain up to 8 decimal places. e.g., 1.00 or 0.0012 or 0.00000065. If you submit a value with more than 8 decimal places, we will round it down to the 8th decimal place.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("unit_price")]
    public UnitPrice7? UnitPrice { get; init; }

    /// <summary>
    /// Set to true to automatically calculate taxes. Site must be configured to use and calculate taxes. If using AvaTax, a tax_code parameter must also be sent.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("taxable")]
    public bool? Taxable { get; init; }

    /// <summary>
    /// A string representing the tax code related to the product type. This is especially important when using AvaTax to tax based on locale. This attribute has a max length of 25 characters.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("tax_code")]
    public string? TaxCode { get; init; }

    /// <summary>
    /// YYYY-MM-DD
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("period_range_start")]
    public string? PeriodRangeStart { get; init; }

    /// <summary>
    /// YYYY-MM-DD
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("period_range_end")]
    public string? PeriodRangeEnd { get; init; }

    /// <summary>
    /// Product handle or product id.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("product_id")]
    public ProductId? ProductId { get; init; }

    /// <summary>
    /// Component handle or component id.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("component_id")]
    public ComponentId3? ComponentId { get; init; }

    /// <summary>
    /// Price point handle or id. For component.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("price_point_id")]
    public PricePointId4? PricePointId { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("product_price_point_id")]
    public ProductPricePointId? ProductPricePointId { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("description")]
    [MaxLength(255)]
    public string? Description { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
