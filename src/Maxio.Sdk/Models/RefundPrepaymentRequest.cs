using System.Text.Json.Serialization;
using Maxio.Core.Models;

namespace Maxio.Models;

public record RefundPrepaymentRequest
{
    [JsonPropertyName("refund")]
    public required RefundPrepayment Refund { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
