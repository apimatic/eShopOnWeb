using System.Text.Json.Serialization;
using Maxio.Core.Models;

namespace Maxio.Models;

public record RecordPaymentRequest
{
    [JsonPropertyName("payment")]
    public required CreatePayment Payment { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
