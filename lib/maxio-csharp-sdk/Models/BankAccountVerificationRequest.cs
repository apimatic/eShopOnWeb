using System.Text.Json.Serialization;
using Maxio.Core.Models;

namespace Maxio.Models;

public record BankAccountVerificationRequest
{
    [JsonPropertyName("bank_account_verification")]
    public required BankAccountVerification BankAccountVerification { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
