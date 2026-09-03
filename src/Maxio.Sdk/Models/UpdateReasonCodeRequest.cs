using System.Text.Json.Serialization;
using Maxio.Core.Models;

namespace Maxio.Models;

public record UpdateReasonCodeRequest
{
    [JsonPropertyName("reason_code")]
    public required UpdateReasonCode ReasonCode { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
