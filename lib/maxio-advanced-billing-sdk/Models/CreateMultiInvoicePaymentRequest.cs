using System.Text.Json.Serialization;
using MaxioAdvancedBilling.Core.Models;

namespace MaxioAdvancedBilling.Models;

public record CreateMultiInvoicePaymentRequest
{
    [JsonPropertyName("payment")]
    public required CreateMultiInvoicePayment Payment { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
