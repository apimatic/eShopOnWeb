using System.Text.Json.Serialization;
using Maxio.Core.Models;

namespace Maxio.Models;

public record GetOneTimeTokenRequest
{
    [JsonPropertyName("payment_profile")]
    public required GetOneTimeTokenPaymentProfile PaymentProfile { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
