using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using Maxio.Core.Models;

namespace Maxio.Models;

/// <summary>
/// Attributes of a draft ad hoc invoice which can be updated. Only the submitted attributes are changed.
/// </summary>
public record UpdateInvoice
{
    /// <summary>
    /// Line item changes to apply. Line items without a <c>uid</c> are added, line items with a <c>uid</c> are updated, and line items with a <c>uid</c> and <c>_destroy</c> set to <c>true</c> are removed. Existing line items not referenced in the array remain unchanged.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("line_items")]
    public IReadOnlyList<UpdateInvoiceItem>? LineItems { get; init; }

    /// <summary>
    /// New issue date for the invoice (format YYYY-MM-DD). This date is interpreted and validated in your site's time zone. It must be today or a date in the past — future dates are not accepted. The due date is recalculated from the issue date and net terms.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("issue_date")]
    public DateTimeOffset? IssueDate { get; init; }

    /// <summary>
    /// Number of days after the issue date on which the invoice is due. The due date is recalculated when net terms or the issue date change.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("net_terms")]
    public int? NetTerms { get; init; }

    /// <summary>
    /// Custom payment instructions displayed on the invoice.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("payment_instructions")]
    public string? PaymentInstructions { get; init; }

    /// <summary>
    /// A custom memo displayed on the invoice.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("memo")]
    public string? Memo { get; init; }

    /// <summary>
    /// Replaces the seller address on the invoice
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("seller_address")]
    public CreateInvoiceAddress? SellerAddress { get; init; }

    /// <summary>
    /// Replaces the billing address on the invoice
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("billing_address")]
    public CreateInvoiceAddress? BillingAddress { get; init; }

    /// <summary>
    /// Replaces the shipping address on the invoice
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("shipping_address")]
    public CreateInvoiceAddress? ShippingAddress { get; init; }

    /// <summary>
    /// When present, replaces all discounts currently applied to the invoice. Send an empty array to remove all discounts.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("coupons")]
    public IReadOnlyList<CreateInvoiceCoupon>? Coupons { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
