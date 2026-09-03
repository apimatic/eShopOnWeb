using System.Text.Json.Serialization;
using Maxio.Core.Models;
using Maxio.Models.Enums;

namespace Maxio.Models;

public record PaymentMethodBankAccount
{
    [JsonPropertyName("masked_account_number")]
    public required string MaskedAccountNumber { get; init; }

    [JsonPropertyName("masked_routing_number")]
    public required string MaskedRoutingNumber { get; init; }

    [JsonPropertyName("type")]
    public required InvoiceEventPaymentMethod Type { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
