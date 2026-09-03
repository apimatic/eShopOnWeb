using System;
using System.Text.Json.Serialization;
using Maxio.Core.Models;
using Maxio.Models.Enums;

namespace Maxio.Models;

/// <summary>
/// Example schema for an <c>refund_invoice</c> event
/// </summary>
public record RefundInvoiceEventData
{
    /// <summary>
    /// If true, credit was created and applied it to the invoice.
    /// </summary>
    [JsonPropertyName("apply_credit")]
    public required bool ApplyCredit { get; init; }

    /// <summary>
    /// Consolidation level of the invoice, which is applicable to invoice consolidation. It will hold one of the following values:
    /// <list type="bullet">
    ///   <item><description>"none": A normal invoice with no consolidation.</description></item>
    ///   <item><description>"child": An invoice segment which has been combined into a consolidated invoice.</description></item>
    ///   <item><description>"parent": A consolidated invoice, whose contents are composed of invoice segments.</description></item>
    /// </list>
    /// <para>
    /// "Parent" invoices do not have lines of their own, but they have subtotals and totals which aggregate the member invoice segments.
    /// </para>
    /// <para>
    /// See also the <see href="https://maxio.zendesk.com/hc/en-us/articles/24252269909389-Invoice-Consolidation">invoice consolidation documentation</see>.
    /// </para>
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("consolidation_level")]
    public InvoiceConsolidationLevel? ConsolidationLevel { get; init; }

    [JsonPropertyName("credit_note_attributes")]
    public required CreditNote CreditNoteAttributes { get; init; }

    /// <summary>
    /// The refund memo.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("memo")]
    public string? Memo { get; init; }

    /// <summary>
    /// The full, original amount of the refund.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("original_amount")]
    public string? OriginalAmount { get; init; }

    /// <summary>
    /// The ID of the payment transaction to be refunded.
    /// </summary>
    [JsonPropertyName("payment_id")]
    public required int PaymentId { get; init; }

    /// <summary>
    /// The amount of the refund.
    /// </summary>
    [JsonPropertyName("refund_amount")]
    public required string RefundAmount { get; init; }

    /// <summary>
    /// The ID of the refund transaction.
    /// </summary>
    [JsonPropertyName("refund_id")]
    public required int RefundId { get; init; }

    /// <summary>
    /// The time the refund was applied, in ISO 8601 format, i.e. "2019-06-07T17:20:06Z"
    /// </summary>
    [JsonPropertyName("transaction_time")]
    public required DateTimeOffset TransactionTime { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
