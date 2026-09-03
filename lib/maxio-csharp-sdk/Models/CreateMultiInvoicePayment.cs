using System.Collections.Generic;
using System.Text.Json.Serialization;
using Maxio.Core.Models;
using Maxio.Models.AnyOf;
using Maxio.Models.Enums;

namespace Maxio.Models;

public record CreateMultiInvoicePayment
{
    /// <summary>
    /// A description to be attached to the payment.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("memo")]
    public string? Memo { get; init; }

    /// <summary>
    /// Additional information related to the payment method (eg. Check #).
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("details")]
    public string? Details { get; init; }

    /// <summary>
    /// The type of payment method used. Defaults to other.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("method")]
    public InvoicePaymentMethodType? Method { get; init; }

    /// <summary>
    /// Dollar amount of the sum of the invoices payment (eg. "10.50" =&gt; $10.50).
    /// </summary>
    [JsonPropertyName("amount")]
    public required Amount1 Amount { get; init; }

    /// <summary>
    /// Date reflecting when the payment was received from a customer. Must be in the past.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("received_on")]
    public string? ReceivedOn { get; init; }

    [JsonPropertyName("applications")]
    public required IReadOnlyList<CreateInvoicePaymentApplication> Applications { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
