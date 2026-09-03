using System.Text.Json.Serialization;
using Maxio.Core.Models;

namespace Maxio.Models;

public record DelayedCancellationResponse
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("message")]
    public string? Message { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
