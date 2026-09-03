using System.Text.Json.Serialization;
using Maxio.Core.Models;

namespace Maxio.Models;

public record MultiInvoicePaymentResponse
{
    [JsonPropertyName("payment")]
    public required MultiInvoicePayment Payment { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
