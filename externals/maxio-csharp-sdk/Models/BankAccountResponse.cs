using System.Text.Json.Serialization;
using Maxio.Core.Models;

namespace Maxio.Models;

public record BankAccountResponse
{
    [JsonPropertyName("payment_profile")]
    public required BankAccountPaymentProfile PaymentProfile { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
