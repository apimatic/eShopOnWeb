using System.Text.Json.Serialization;
using Maxio.Core.Models;

namespace Maxio.Models;

public record ReasonCodeResponse
{
    [JsonPropertyName("reason_code")]
    public required ReasonCode ReasonCode { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
