using System;
using System.Text.Json.Serialization;
using Maxio.Core.Models;

namespace Maxio.Models;

/// <summary>
/// Example schema for an <c>apply_debit_note</c> event
/// </summary>
public record ApplyDebitNoteEventData
{
    /// <summary>
    /// A unique, identifying string that appears on the debit note and in places it is referenced.
    /// </summary>
    [JsonPropertyName("debit_note_number")]
    public required string DebitNoteNumber { get; init; }

    /// <summary>
    /// Unique identifier for the debit note. It is generated automatically by Chargify and has the prefix "db_" followed by alphanumeric characters.
    /// </summary>
    [JsonPropertyName("debit_note_uid")]
    public required string DebitNoteUid { get; init; }

    /// <summary>
    /// The full, original amount of the debit note.
    /// </summary>
    [JsonPropertyName("original_amount")]
    public required string OriginalAmount { get; init; }

    /// <summary>
    /// The amount of the debit note applied to invoice.
    /// </summary>
    [JsonPropertyName("applied_amount")]
    public required string AppliedAmount { get; init; }

    /// <summary>
    /// The debit note memo.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("memo")]
    public string? Memo { get; init; }

    /// <summary>
    /// The time the debit note was applied, in ISO 8601 format, i.e. "2019-06-07T17:20:06Z"
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("transaction_time")]
    public DateTimeOffset? TransactionTime { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
