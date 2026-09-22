using System.Text.Json.Serialization;
using MaxioAdvancedBilling.Core.Models;

namespace MaxioAdvancedBilling.Models;

public record BankAccountResponse
{
    [JsonPropertyName("payment_profile")]
    public required BankAccountPaymentProfile PaymentProfile { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
