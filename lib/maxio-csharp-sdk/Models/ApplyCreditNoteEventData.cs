using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using Maxio.Core.Models;

namespace Maxio.Models;

/// <summary>
/// Example schema for an <c>apply_credit_note</c> event
/// </summary>
public record ApplyCreditNoteEventData
{
    /// <summary>
    /// Unique identifier for the credit note application. It is generated automatically by Chargify and has the prefix "cdt_" followed by alphanumeric characters.
    /// </summary>
    [JsonPropertyName("uid")]
    public required string Uid { get; init; }

    /// <summary>
    /// A unique, identifying string that appears on the credit note and in places it is referenced.
    /// </summary>
    [JsonPropertyName("credit_note_number")]
    public required string CreditNoteNumber { get; init; }

    /// <summary>
    /// Unique identifier for the credit note. It is generated automatically by Chargify and has the prefix "cn_" followed by alphanumeric characters.
    /// </summary>
    [JsonPropertyName("credit_note_uid")]
    public required string CreditNoteUid { get; init; }

    /// <summary>
    /// The full, original amount of the credit note.
    /// </summary>
    [JsonPropertyName("original_amount")]
    public required string OriginalAmount { get; init; }

    /// <summary>
    /// The amount of the credit note applied to invoice.
    /// </summary>
    [JsonPropertyName("applied_amount")]
    public required string AppliedAmount { get; init; }

    /// <summary>
    /// The time the credit note was applied, in ISO 8601 format, i.e. "2019-06-07T17:20:06Z"
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("transaction_time")]
    public DateTimeOffset? TransactionTime { get; init; }

    /// <summary>
    /// The credit note memo.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("memo")]
    public string? Memo { get; init; }

    /// <summary>
    /// The role of the credit note (e.g. 'general')
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("role")]
    public string? Role { get; init; }

    /// <summary>
    /// Shows whether it was applied to consolidated invoice or not.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("consolidated_invoice")]
    public bool? ConsolidatedInvoice { get; init; }

    /// <summary>
    /// List of credit notes applied to children invoices (if consolidated invoice)
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("applied_credit_notes")]
    public IReadOnlyList<AppliedCreditNoteData>? AppliedCreditNotes { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
