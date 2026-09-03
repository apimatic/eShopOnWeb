using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using Maxio.Core.Models;
using Maxio.Models.Enums;

namespace Maxio.Models;

public record CreditNote
{
    /// <summary>
    /// Unique identifier for the credit note. It is generated automatically by Chargify and has the prefix "cn_" followed by alphanumeric characters.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("uid")]
    public string? Uid { get; init; }

    /// <summary>
    /// ID of the site to which the credit note belongs.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("site_id")]
    public int? SiteId { get; init; }

    /// <summary>
    /// ID of the customer to which the credit note belongs.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("customer_id")]
    public int? CustomerId { get; init; }

    /// <summary>
    /// ID of the subscription that generated the credit note.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("subscription_id")]
    public int? SubscriptionId { get; init; }

    /// <summary>
    /// A unique, identifying string that appears on the credit note and in places it is referenced.
    /// <para>
    /// While the UID is long and not appropriate to show to customers, the number is usually shorter and consumable by the customer and the merchant alike.
    /// </para>
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("number")]
    public string? Number { get; init; }

    /// <summary>
    /// A monotonically increasing number assigned to credit notes as they are created.  This number is unique within a site and can be used to sort and order credit notes.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("sequence_number")]
    public int? SequenceNumber { get; init; }

    /// <summary>
    /// Date the credit note was issued to the customer.  This is the date that the credit was made available for application, and may come before it is fully applied.
    /// <para>
    /// The format is <c>"YYYY-MM-DD"</c>.
    /// </para>
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("issue_date")]
    public DateTimeOffset? IssueDate { get; init; }

    /// <summary>
    /// Credit notes are applied to invoices to offset invoiced amounts - they reduce the amount due. This field is the date the credit note became fully applied to invoices.
    /// <para>
    /// If the credit note has been partially applied, this field will not have a value until it has been fully applied.
    /// </para>
    /// <para>
    /// The format is <c>"YYYY-MM-DD"</c>.
    /// </para>
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("applied_date")]
    public DateTimeOffset? AppliedDate { get; init; }

    /// <summary>
    /// Current status of the credit note.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("status")]
    public CreditNoteStatus? Status { get; init; }

    /// <summary>
    /// The ISO 4217 currency code (3 character string) representing the currency of the credit note amount fields.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("currency")]
    public string? Currency { get; init; }

    /// <summary>
    /// The memo printed on credit note, which is a description of the reason for the credit.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("memo")]
    public string? Memo { get; init; }

    /// <summary>
    /// Information about the seller (merchant) listed on the masthead of the credit note.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("seller")]
    public InvoiceSeller? Seller { get; init; }

    /// <summary>
    /// Information about the customer who is owner or recipient of the credited subscription.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("customer")]
    public InvoiceCustomer? Customer { get; init; }

    /// <summary>
    /// The billing address of the credit subscription.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("billing_address")]
    public InvoiceAddress? BillingAddress { get; init; }

    /// <summary>
    /// The shipping address of the credited subscription.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("shipping_address")]
    public InvoiceAddress? ShippingAddress { get; init; }

    /// <summary>
    /// Subtotal of the credit note, which is the sum of all line items before discounts or taxes. Note that this is a positive amount representing the credit back to the customer.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("subtotal_amount")]
    public string? SubtotalAmount { get; init; }

    /// <summary>
    /// Total discount applied to the credit note. Note that this is a positive amount representing the discount amount being credited back to the customer (i.e., a credit on an earlier discount). For example, if the original purchase was $1.00 and the original discount was $0.10, a credit of $0.50 of the original purchase (half) would have a discount credit of $0.05 (also half).
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("discount_amount")]
    public string? DiscountAmount { get; init; }

    /// <summary>
    /// Total tax of the credit note. Note that this is a positive amount representing a previously taxed amount being credited back to the customer (i.e., a credit of an earlier tax). For example, if the original purchase was $1.00 and the original tax was $0.10, a credit of $0.50 of the original purchase (half) would also have a tax credit of $0.05 (also half).
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("tax_amount")]
    public string? TaxAmount { get; init; }

    /// <summary>
    /// The credit note total, which is <c>subtotal_amount - discount_amount + tax_amount</c>.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("total_amount")]
    public string? TotalAmount { get; init; }

    /// <summary>
    /// The amount of the credit note that has already been applied to invoices.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("applied_amount")]
    public string? AppliedAmount { get; init; }

    /// <summary>
    /// The amount of the credit note remaining to be applied to invoices, which is <c>total_amount - applied_amount</c>.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("remaining_amount")]
    public string? RemainingAmount { get; init; }

    /// <summary>
    /// Line items on the credit note.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("line_items")]
    public IReadOnlyList<CreditNoteLineItem>? LineItems { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("discounts")]
    public IReadOnlyList<InvoiceDiscount>? Discounts { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("taxes")]
    public IReadOnlyList<InvoiceTax>? Taxes { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("applications")]
    public IReadOnlyList<CreditNoteApplication>? Applications { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("refunds")]
    public IReadOnlyList<InvoiceRefund>? Refunds { get; init; }

    /// <summary>
    /// An array of origin invoices for the credit note. Learn more about <see href="https://maxio.zendesk.com/hc/en-us/articles/24252261284749-Credit-Notes-Proration#origin-invoices">Origin Invoice from our docs</see>.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("origin_invoices")]
    public IReadOnlyList<OriginInvoice>? OriginInvoices { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
