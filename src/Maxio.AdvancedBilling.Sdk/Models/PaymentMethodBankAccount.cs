using System.Text.Json.Serialization;
using MaxioAdvancedBilling.Core.Models;
using MaxioAdvancedBilling.Models.Enums;

namespace MaxioAdvancedBilling.Models;

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
