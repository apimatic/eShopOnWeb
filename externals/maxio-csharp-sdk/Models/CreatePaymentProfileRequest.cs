using System.Text.Json.Serialization;
using Maxio.Core.Models;

namespace Maxio.Models;

public record CreatePaymentProfileRequest
{
    [JsonPropertyName("payment_profile")]
    public required CreatePaymentProfile PaymentProfile { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
