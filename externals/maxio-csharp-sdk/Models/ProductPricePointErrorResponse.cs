using System.Text.Json.Serialization;
using Maxio.Core.Models;

namespace Maxio.Models;

public record ProductPricePointErrorResponse
{
    [JsonPropertyName("errors")]
    public required ProductPricePointErrors Errors { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
