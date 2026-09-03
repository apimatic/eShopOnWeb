using System.Text.Json.Serialization;
using Maxio.Core.Models;

namespace Maxio.Models;

public record CancellationRequest
{
    [JsonPropertyName("subscription")]
    public required CancellationOptions Subscription { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
