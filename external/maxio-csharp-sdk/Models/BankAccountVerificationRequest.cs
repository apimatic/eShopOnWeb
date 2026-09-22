using System.Text.Json.Serialization;
using MaxioAdvancedBilling.Core.Models;

namespace MaxioAdvancedBilling.Models;

public record BankAccountVerificationRequest
{
    [JsonPropertyName("bank_account_verification")]
    public required BankAccountVerification BankAccountVerification { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
