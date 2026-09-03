using System.Text.Json.Serialization;
using Maxio.Core.Models;
using Maxio.Models.AnyOf;

namespace Maxio.Models;

/// <summary>
/// Refund consolidated invoice.
/// </summary>
public record RefundConsolidatedInvoice
{
    /// <summary>
    /// A description for the refund
    /// </summary>
    [JsonPropertyName("memo")]
    public required string Memo { get; init; }

    /// <summary>
    /// The ID of the payment to be refunded
    /// </summary>
    [JsonPropertyName("payment_id")]
    public required int PaymentId { get; init; }

    /// <summary>
    /// An array of segment uids to refund or the string 'all' to indicate that all segments should be refunded
    /// </summary>
    [JsonPropertyName("segment_uids")]
    public required SegmentUids SegmentUids { get; init; }

    /// <summary>
    /// Flag that marks refund as external (no money is returned to the customer). Defaults to <c>false</c>.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("external")]
    public bool? External { get; init; }

    /// <summary>
    /// If set to true, creates credit and applies it to an invoice. Defaults to <c>false</c>.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("apply_credit")]
    public bool? ApplyCredit { get; init; }

    /// <summary>
    /// The amount of payment to be refunded in decimal format. Example: "10.50". This will default to the full amount of the payment if not provided.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("amount")]
    public string? Amount { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
