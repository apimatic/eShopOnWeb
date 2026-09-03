using System;
using System.Text.Json.Serialization;
using Maxio.Core.Models;
using Maxio.Models.Enums;
using Maxio.Models.OneOf;

namespace Maxio.Models;

/// <summary>
/// Example schema for an <c>apply_payment</c> event
/// </summary>
public record ApplyPaymentEventData
{
    [JsonPropertyName("consolidation_level")]
    public required InvoiceConsolidationLevel ConsolidationLevel { get; init; }

    /// <summary>
    /// The payment memo
    /// </summary>
    [JsonPropertyName("memo")]
    public required string Memo { get; init; }

    /// <summary>
    /// The full, original amount of the payment transaction as a string in full units. Incoming payments can be split amongst several invoices, which will result in a <c>applied_amount</c> less than the <c>original_amount</c>. Example: A $100.99 payment, of which $40.11 is applied to this invoice, will have an <c>original_amount</c> of <c>"100.99"</c>.
    /// </summary>
    [JsonPropertyName("original_amount")]
    public required string OriginalAmount { get; init; }

    /// <summary>
    /// The amount of the payment applied to this invoice. Incoming payments can be split amongst several invoices, which will result in a <c>applied_amount</c> less than the <c>original_amount</c>. Example: A $100.99 payment, of which $40.11 is applied to this invoice, will have an <c>applied_amount</c> of <c>"40.11"</c>.
    /// </summary>
    [JsonPropertyName("applied_amount")]
    public required string AppliedAmount { get; init; }

    /// <summary>
    /// The time the payment was applied, in ISO 8601 format, i.e. "2019-06-07T17:20:06Z"
    /// </summary>
    [JsonPropertyName("transaction_time")]
    public required DateTimeOffset TransactionTime { get; init; }

    /// <summary>
    /// A nested data structure detailing the method of payment
    /// </summary>
    [JsonPropertyName("payment_method")]
    public required InvoiceEventPayment PaymentMethod { get; init; }

    /// <summary>
    /// The Chargify id of the original payment
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("transaction_id")]
    public int? TransactionId { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("parent_invoice_number")]
    public int? ParentInvoiceNumber { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("remaining_prepayment_amount")]
    public string? RemainingPrepaymentAmount { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("prepayment")]
    public bool? Prepayment { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("external")]
    public bool? External { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
