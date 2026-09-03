using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using PayPal.Core.Models;
using PayPal.Core.Validation;
using PayPal.Core.Validation.Attributes;

namespace PayPal.Models;

/// <summary>
/// The line items for this purchase. If your merchant account has been configured for Level 3 processing this field will be passed to the processor on your behalf.
/// </summary>
public record LineItem
{
    /// <summary>
    /// The item name or title.
    /// </summary>
    [JsonPropertyName("name")]
    [StringLength(127, MinimumLength = 1)]
    public required string Name { get; init; }

    /// <summary>
    /// The item quantity. Must be a whole number.
    /// </summary>
    [JsonPropertyName("quantity")]
    [MaxLength(10)]
    [RegularExpression("^[1-9][0-9]{0,9}$")]
    public required string Quantity { get; init; }

    /// <summary>
    /// The detailed item description.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("description")]
    [MaxLength(2048)]
    public string? Description { get; init; }

    /// <summary>
    /// The stock keeping unit (SKU) for the item.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("sku")]
    [MaxLength(127)]
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
    [RegularExpression("^(https:)([/|.|\\w|\\s|-])*\\.(?:jpg|gif|png|jpeg|JPG|GIF|PNG|JPEG)(\\?.*)?$")]
    [Format(FormatKind.Uri)]
    public string? ImageUrl { get; init; }

    /// <summary>
    /// The Universal Product Code of the item.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("upc")]
    public UniversalProductCode? Upc { get; init; }

    /// <summary>
    /// Metadata for merchant-managed recurring billing plans. Valid only during the saved payment method token or billing agreement creation.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("billing_plan")]
    public OrderBillingPlan? BillingPlan { get; init; }

    /// <summary>
    /// The currency and amount for a financial transaction, such as a balance or payment due.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("unit_amount")]
    public Money? UnitAmount { get; init; }

    /// <summary>
    /// The currency and amount for a financial transaction, such as a balance or payment due.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("tax")]
    public Money? Tax { get; init; }

    /// <summary>
    /// Code used to classify items purchased and track the total amount spent across various categories of products and services. Different corporate purchasing organizations may use different standards, but the United Nations Standard Products and Services Code (UNSPSC) is frequently used.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("commodity_code")]
    [StringLength(12, MinimumLength = 1)]
    [RegularExpression("^[a-zA-Z0-9_'.-]*$")]
    public string? CommodityCode { get; init; }

    /// <summary>
    /// The currency and amount for a financial transaction, such as a balance or payment due.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("discount_amount")]
    public Money? DiscountAmount { get; init; }

    /// <summary>
    /// The currency and amount for a financial transaction, such as a balance or payment due.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("total_amount")]
    public Money? TotalAmount { get; init; }

    /// <summary>
    /// Unit of measure is a standard used to express the magnitude of a quantity in international trade. Most commonly used (but not limited to) examples are: Acre (ACR), Ampere (AMP), Centigram (CGM), Centimetre (CMT), Cubic inch (INQ), Cubic metre (MTQ), Fluid ounce (OZA), Foot (FOT), Hour (HUR), Item (ITM), Kilogram (KGM), Kilometre (KMT), Kilowatt (KWT), Liquid gallon (GLL), Liter (LTR), Pounds (LBS), Square foot (FTK).
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("unit_of_measure")]
    [StringLength(12, MinimumLength = 1)]
    [RegularExpression("^[a-zA-Z0-9_'.-]*$")]
    public string? UnitOfMeasure { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
