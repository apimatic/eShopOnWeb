using System.Text.Json.Serialization;
using Maxio.Core.Models;

namespace Maxio.Models;

public record PrepaymentResponse
{
    [JsonPropertyName("prepayment")]
    public required Prepayment Prepayment { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
