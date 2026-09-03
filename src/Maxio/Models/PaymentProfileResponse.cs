using System.Text.Json.Serialization;
using Maxio.Core.Models;
using Maxio.Models.OneOf;

namespace Maxio.Models;

public record PaymentProfileResponse
{
    [JsonPropertyName("payment_profile")]
    public required PaymentProfile PaymentProfile { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
