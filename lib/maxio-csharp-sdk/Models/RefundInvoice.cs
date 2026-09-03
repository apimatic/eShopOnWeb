using System.Text.Json.Serialization;
using Maxio.Core.Models;

namespace Maxio.Models;

/// <summary>
/// Refund an invoice or a segment of a consolidated invoice.
/// </summary>
public record RefundInvoice
{
    /// <summary>
    /// The amount to be refunded in decimal format as a string. Example: "10.50". Must not exceed the remaining refundable balance of the payment.
    /// </summary>
    [JsonPropertyName("amount")]
    public required string Amount { get; init; }

    /// <summary>
    /// A description that will be attached to the refund
    /// </summary>
    [JsonPropertyName("memo")]
    public required string Memo { get; init; }

    /// <summary>
    /// The ID of the payment to be refunded
    /// </summary>
    [JsonPropertyName("payment_id")]
    public required int PaymentId { get; init; }

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
    /// If <c>apply_credit</c> is set to false and refunding full amount, if <c>void_invoice</c> is set to true, invoice will be voided after refund. Defaults to <c>false</c>.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("void_invoice")]
    public bool? VoidInvoice { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
