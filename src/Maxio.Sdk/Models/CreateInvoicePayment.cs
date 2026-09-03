using System;
using System.Text.Json.Serialization;
using Maxio.Core.Models;
using Maxio.Models.AnyOf;
using Maxio.Models.Enums;

namespace Maxio.Models;

public record CreateInvoicePayment
{
    /// <summary>
    /// A string of the dollar amount to be refunded (eg. "10.50" =&gt; $10.50)
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("amount")]
    public Amount? Amount { get; init; }

    /// <summary>
    /// A description to be attached to the payment. Applicable only to <c>external</c> payments.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("memo")]
    public string? Memo { get; init; }

    /// <summary>
    /// The type of payment method used. Defaults to other.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("method")]
    public InvoicePaymentMethodType? Method { get; init; }

    /// <summary>
    /// Additional information related to the payment method (eg. Check #). Applicable only to <c>external</c> payments.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("details")]
    public string? Details { get; init; }

    /// <summary>
    /// The ID of the payment profile to be used for the payment.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("payment_profile_id")]
    public int? PaymentProfileId { get; init; }

    /// <summary>
    /// Date reflecting when the payment was received from a customer. Must be in the past. Applicable only to
    /// <c>external</c> payments.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("received_on")]
    public DateTimeOffset? ReceivedOn { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
