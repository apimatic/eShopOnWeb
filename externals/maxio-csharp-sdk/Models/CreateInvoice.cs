using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using Maxio.Core.Models;
using Maxio.Models.Enums;

namespace Maxio.Models;

public record CreateInvoice
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("line_items")]
    public IReadOnlyList<CreateInvoiceItem>? LineItems { get; init; }

    /// <summary>
    /// Date on which the invoice will be issued (format YYYY-MM-DD). This date is interpreted and validated in your site's time zone. It must be today or a date in the past — future dates are not accepted. If omitted, defaults to today in your site's time zone.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("issue_date")]
    public DateTimeOffset? IssueDate { get; init; }

    /// <summary>
    /// By default, invoices will be created with a due date matching the date of invoice creation. If a different due date is desired, the net_terms parameter can be sent indicating the number of days in advance the due date should be.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("net_terms")]
    public int? NetTerms { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("payment_instructions")]
    public string? PaymentInstructions { get; init; }

    /// <summary>
    /// A custom memo can be sent to override the site's default.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("memo")]
    public string? Memo { get; init; }

    /// <summary>
    /// Overrides the defaults for the site.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("seller_address")]
    public CreateInvoiceAddress? SellerAddress { get; init; }

    /// <summary>
    /// Overrides the default for the customer.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("billing_address")]
    public CreateInvoiceAddress? BillingAddress { get; init; }

    /// <summary>
    /// Overrides the default for the customer.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("shipping_address")]
    public CreateInvoiceAddress? ShippingAddress { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("coupons")]
    public IReadOnlyList<CreateInvoiceCoupon>? Coupons { get; init; }

    [JsonPropertyName("status")]
    public CreateInvoiceStatus? Status { get; init; } = CreateInvoiceStatus.Open;

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
