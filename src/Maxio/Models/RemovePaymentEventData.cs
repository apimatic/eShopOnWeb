using System;
using System.Text.Json.Serialization;
using Maxio.Core.Models;
using Maxio.Models.OneOf;

namespace Maxio.Models;

/// <summary>
/// Example schema for an <c>remove_payment</c> event
/// </summary>
public record RemovePaymentEventData
{
    /// <summary>
    /// Transaction ID of the original payment that was removed
    /// </summary>
    [JsonPropertyName("transaction_id")]
    public required int TransactionId { get; init; }

    /// <summary>
    /// Memo of the original payment
    /// </summary>
    [JsonPropertyName("memo")]
    public required string Memo { get; init; }

    /// <summary>
    /// Full amount of the original payment
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("original_amount")]
    public string? OriginalAmount { get; init; }

    /// <summary>
    /// Applied amount of the original payment
    /// </summary>
    [JsonPropertyName("applied_amount")]
    public required string AppliedAmount { get; init; }

    /// <summary>
    /// Transaction time of the original payment, in ISO 8601 format, i.e. "2019-06-07T17:20:06Z"
    /// </summary>
    [JsonPropertyName("transaction_time")]
    public required DateTimeOffset TransactionTime { get; init; }

    /// <summary>
    /// A nested data structure detailing the method of payment
    /// </summary>
    [JsonPropertyName("payment_method")]
    public required InvoiceEventPayment PaymentMethod { get; init; }

    /// <summary>
    /// The flag that shows whether the original payment was a prepayment or not
    /// </summary>
    [JsonPropertyName("prepayment")]
    public required bool Prepayment { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
