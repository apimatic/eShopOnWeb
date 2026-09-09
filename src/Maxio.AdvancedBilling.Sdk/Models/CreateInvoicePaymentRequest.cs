using System.Text.Json.Serialization;
using MaxioAdvancedBilling.Core.Models;
using MaxioAdvancedBilling.Models.Enums;

namespace MaxioAdvancedBilling.Models;

public record CreateInvoicePaymentRequest
{
    [JsonPropertyName("payment")]
    public required CreateInvoicePayment Payment { get; init; }

    /// <summary>
    /// The type of payment to be applied to an Invoice. Defaults to external.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("type")]
    public InvoicePaymentType? Type { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
