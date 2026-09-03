using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using Maxio.Core.Models;
using Maxio.Models.Enums;

namespace Maxio.Models;

public record DebitNote
{
    /// <summary>
    /// Unique identifier for the debit note. It is generated automatically by Chargify and has the prefix "db_" followed by alphanumeric characters.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("uid")]
    public string? Uid { get; init; }

    /// <summary>
    /// ID of the site to which the debit note belongs.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("site_id")]
    public int? SiteId { get; init; }

    /// <summary>
    /// ID of the customer to which the debit note belongs.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("customer_id")]
    public int? CustomerId { get; init; }

    /// <summary>
    /// ID of the subscription that generated the debit note.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("subscription_id")]
    public int? SubscriptionId { get; init; }

    /// <summary>
    /// A unique identifier that appears on the debit note and in places it is referenced.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("number")]
    public int? Number { get; init; }

    /// <summary>
    /// A monotonically increasing number assigned to debit notes as they are created.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("sequence_number")]
    public int? SequenceNumber { get; init; }

    /// <summary>
    /// Unique identifier for the connected credit note. It is generated automatically by Chargify and has the prefix "cn_" followed by alphanumeric characters.
    /// <para>
    /// While the UID is long and not appropriate to show to customers, the number is usually shorter and consumable by the customer and the merchant alike.
    /// </para>
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("origin_credit_note_uid")]
    public string? OriginCreditNoteUid { get; init; }

    /// <summary>
    /// A unique identifying string of the connected credit note.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("origin_credit_note_number")]
    public string? OriginCreditNoteNumber { get; init; }

    /// <summary>
    /// Date the document was issued to the customer. This is the date that the document was made available for payment.
    /// <para>
    /// The format is "YYYY-MM-DD".
    /// </para>
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("issue_date")]
    public DateTimeOffset? IssueDate { get; init; }

    /// <summary>
    /// Debit notes are applied to invoices to offset invoiced amounts - they adjust the amount due. This field is the date the debit note document became fully applied to the invoice.
    /// <para>
    /// The format is "YYYY-MM-DD".
    /// </para>
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("applied_date")]
    public DateTimeOffset? AppliedDate { get; init; }

    /// <summary>
    /// Date the document is due for payment. The format is "YYYY-MM-DD".
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("due_date")]
    public DateTimeOffset? DueDate { get; init; }

    /// <summary>
    /// Current status of the debit note.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("status")]
    public DebitNoteStatus? Status { get; init; }

    /// <summary>
    /// The memo printed on debit note, which is a description of the reason for the debit.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("memo")]
    public string? Memo { get; init; }

    /// <summary>
    /// The role of the debit note.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("role")]
    public DebitNoteRole? Role { get; init; }

    /// <summary>
    /// The ISO 4217 currency code (3 character string) representing the currency of the credit note amount fields.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("currency")]
    public string? Currency { get; init; }

    /// <summary>
    /// Information about the seller (merchant) listed on the masthead of the debit note.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("seller")]
    public InvoiceSeller? Seller { get; init; }

    /// <summary>
    /// Information about the customer who is the owner or recipient of the debited subscription.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("customer")]
    public InvoiceCustomer? Customer { get; init; }

    /// <summary>
    /// The billing address of the debited subscription.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("billing_address")]
    public InvoiceAddress? BillingAddress { get; init; }

    /// <summary>
    /// The shipping address of the debited subscription.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("shipping_address")]
    public InvoiceAddress? ShippingAddress { get; init; }

    /// <summary>
    /// Line items on the debit note.
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
    [JsonPropertyName("refunds")]
    public IReadOnlyList<InvoiceRefund>? Refunds { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
