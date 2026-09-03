using System;
using System.Text.Json.Serialization;
using Maxio.Core.Models;

namespace Maxio.Models;

/// <summary>
/// Example schema for an <c>void_invoice</c> event
/// </summary>
public record VoidInvoiceEventData
{
    [JsonPropertyName("credit_note_attributes")]
    public required CreditNote? CreditNoteAttributes { get; init; }

    /// <summary>
    /// The memo provided during invoice voiding.
    /// </summary>
    [JsonPropertyName("memo")]
    public required string? Memo { get; init; }

    /// <summary>
    /// The amount of the void.
    /// </summary>
    [JsonPropertyName("applied_amount")]
    public required string? AppliedAmount { get; init; }

    /// <summary>
    /// The time the refund was applied, in ISO 8601 format, i.e. "2019-06-07T17:20:06Z"
    /// </summary>
    [JsonPropertyName("transaction_time")]
    public required DateTimeOffset? TransactionTime { get; init; }

    /// <summary>
    /// If true, the invoice is an advance invoice.
    /// </summary>
    [JsonPropertyName("is_advance_invoice")]
    public required bool IsAdvanceInvoice { get; init; }

    /// <summary>
    /// The reason for the void.
    /// </summary>
    [JsonPropertyName("reason")]
    public required string Reason { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
