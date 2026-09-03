using System.Text.Json.Serialization;
using Maxio.Core.Models;

namespace Maxio.Models;

public record CreatePrepaymentRequest
{
    [JsonPropertyName("prepayment")]
    public required CreatePrepayment Prepayment { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
