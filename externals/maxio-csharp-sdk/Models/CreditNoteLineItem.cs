using System;
using System.Text.Json.Serialization;
using Maxio.Core.Models;

namespace Maxio.Models;

public record CreditNoteLineItem
{
    /// <summary>
    /// Unique identifier for the line item.  Useful when cross-referencing the line against individual discounts in the <c>discounts</c> or <c>taxes</c> lists.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("uid")]
    public string? Uid { get; init; }

    /// <summary>
    /// A short descriptor for the credit given by this line.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("title")]
    public string? Title { get; init; }

    /// <summary>
    /// Detailed description for the credit given by this line.  May include proration details in plain text.
    /// <para>
    /// Note: this string may contain line breaks that are hints for the best display format on the credit note.
    /// </para>
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("description")]
    public string? Description { get; init; }

    /// <summary>
    /// The quantity or count of units credited by the line item.
    /// <para>
    /// This is a decimal number represented as a string. (See "About Decimal Numbers".)
    /// </para>
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("quantity")]
    public string? Quantity { get; init; }

    /// <summary>
    /// The price per unit for the line item.
    /// <para>
    /// When tiered pricing was used (i.e., not every unit was actually priced at the same price) this will be the blended average cost per unit and the <c>tiered_unit_price</c> field will be set to <c>true</c>.
    /// </para>
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("unit_price")]
    public string? UnitPrice { get; init; }

    /// <summary>
    /// The line subtotal, generally calculated as <c>quantity * unit_price</c>. This is the canonical amount of record for the line - when rounding differences are in play, <c>subtotal_amount</c> takes precedence over the value derived from <c>quantity * unit_price</c> (which may not have the proper precision to exactly equal this amount).
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("subtotal_amount")]
    public string? SubtotalAmount { get; init; }

    /// <summary>
    /// The approximate discount of just this line.
    /// <para>
    /// The value is approximated in cases where rounding errors make it difficult to apportion exactly a total discount among many lines. Several lines may have been summed prior to applying the discount to arrive at <c>discount_amount</c> for the invoice - backing that out to the discount on a single line may introduce rounding or precision errors.
    /// </para>
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("discount_amount")]
    public string? DiscountAmount { get; init; }

    /// <summary>
    /// The approximate tax of just this line.
    /// <para>
    /// The value is approximated in cases where rounding errors make it difficult to apportion exactly a total tax among many lines. Several lines may have been summed prior to applying the tax rate to arrive at <c>tax_amount</c> for the invoice - backing that out to the tax on a single line may introduce rounding or precision errors.
    /// </para>
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("tax_amount")]
    public string? TaxAmount { get; init; }

    /// <summary>
    /// Whether the unit price for this line item is tax-inclusive.
    /// <para>
    /// When <c>true</c>, <c>unit_price</c> already includes tax and <c>tax_amount</c> represents the portion of the price attributable to tax. When <c>false</c>, any applicable tax is added on top of the price.
    /// </para>
    /// <para>
    /// The value is inherited from the source price point's <c>tax_included</c> setting. Custom or ad-hoc line items (which have no associated price point) always return <c>false</c>.
    /// </para>
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("tax_included")]
    public bool? TaxIncluded { get; init; }

    /// <summary>
    /// The non-canonical total amount for the line.
    /// <para>
    /// <c>subtotal_amount</c> is the canonical amount for a line. The invoice <c>total_amount</c> is derived from the sum of the line <c>subtotal_amount</c>s and discounts or taxes applied thereafter.  Therefore, due to rounding or precision errors, the sum of line <c>total_amount</c>s may not equal the invoice <c>total_amount</c>.
    /// </para>
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("total_amount")]
    public string? TotalAmount { get; init; }

    /// <summary>
    /// When <c>true</c>, indicates that the actual pricing scheme for the line was tiered, so the <c>unit_price</c> shown is the blended average for all units.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("tiered_unit_price")]
    public bool? TieredUnitPrice { get; init; }

    /// <summary>
    /// Start date for the period credited by this line. The format is <c>"YYYY-MM-DD"</c>.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("period_range_start")]
    public DateTimeOffset? PeriodRangeStart { get; init; }

    /// <summary>
    /// End date for the period credited by this line. The format is <c>"YYYY-MM-DD"</c>.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("period_range_end")]
    public DateTimeOffset? PeriodRangeEnd { get; init; }

    /// <summary>
    /// The ID of the product being credited.
    /// <para>
    /// This may be set even for component credits, so true product-only (non-component) credits will also have a nil <c>component_id</c>.
    /// </para>
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("product_id")]
    public int? ProductId { get; init; }

    /// <summary>
    /// The version of the product being credited.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("product_version")]
    public int? ProductVersion { get; init; }

    /// <summary>
    /// The ID of the component being credited. Will be <c>nil</c> for non-component credits.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("component_id")]
    public int? ComponentId { get; init; }

    /// <summary>
    /// The price point ID of the component being credited. Will be <c>nil</c> for non-component credits.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("price_point_id")]
    public int? PricePointId { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("billing_schedule_item_id")]
    public int? BillingScheduleItemId { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("custom_item")]
    public bool? CustomItem { get; init; }

    /// <summary>
    /// The date a prepaid allocation is set to expire. Only present on line items representing prepaid component allocations. The format is <c>"YYYY-MM-DD"</c>.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("prepaid_allocation_expires_at")]
    public DateTimeOffset? PrepaidAllocationExpiresAt { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
