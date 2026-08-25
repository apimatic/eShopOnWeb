using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace PayPalServerSdk.Models;

/// <summary>
/// Captures either a portion or the full authorized amount of an authorized payment.
/// </summary>
public record CaptureRequest
{
    /// <summary>
    /// The currency and amount for a financial transaction, such as a balance or payment due.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("amount")]
    public Money? Amount { get; init; }

    /// <summary>
    /// The API caller-provided external invoice number for this order. Appears in both the payer's transaction history and the emails that the payer receives.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("invoice_id")]
    [MaxLength(127)]
    public string? InvoiceId { get; init; }

    /// <summary>
    /// Indicates whether you can make additional captures against the authorized payment. Set to <c>true</c> if you do not intend to capture additional payments against the authorization. Set to <c>false</c> if you intend to capture additional payments against the authorization.
    /// </summary>
    [JsonPropertyName("final_capture")]
    public bool? FinalCapture { get; init; } = false;

    /// <summary>
    /// Any additional payment instructions to be consider during payment processing. This processing instruction is applicable for Capturing an order or Authorizing an Order.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("payment_instruction")]
    public CapturePaymentInstruction? PaymentInstruction { get; init; }

    /// <summary>
    /// An informational note about this settlement. Appears in both the payer's transaction history and the emails that the payer receives.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("note_to_payer")]
    [MaxLength(255)]
    public string? NoteToPayer { get; init; }

    /// <summary>
    /// The payment descriptor on the payer's account statement.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("soft_descriptor")]
    [MaxLength(22)]
    public string? SoftDescriptor { get; init; }
}
