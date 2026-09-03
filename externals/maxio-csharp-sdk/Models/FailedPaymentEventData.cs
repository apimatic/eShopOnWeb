using System.Text.Json.Serialization;
using Maxio.Core.Models;
using Maxio.Models.Enums;

namespace Maxio.Models;

/// <summary>
/// Example schema for an <c>failed_payment</c> event
/// </summary>
public record FailedPaymentEventData
{
    /// <summary>
    /// The monetary value of the payment, expressed in cents.
    /// </summary>
    [JsonPropertyName("amount_in_cents")]
    public required int AmountInCents { get; init; }

    /// <summary>
    /// The monetary value of the payment, expressed in dollars.
    /// </summary>
    [JsonPropertyName("applied_amount")]
    public required int AppliedAmount { get; init; }

    /// <summary>
    /// The memo passed when the payment was created.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("memo")]
    public string? Memo { get; init; }

    [JsonPropertyName("payment_method")]
    public required InvoicePaymentMethodType PaymentMethod { get; init; }

    /// <summary>
    /// The transaction ID of the failed payment.
    /// </summary>
    [JsonPropertyName("transaction_id")]
    public required int TransactionId { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
