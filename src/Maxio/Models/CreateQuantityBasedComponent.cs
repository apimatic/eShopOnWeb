using System.Text.Json.Serialization;
using Maxio.Core.Models;

namespace Maxio.Models;

public record CreateQuantityBasedComponent
{
    [JsonPropertyName("quantity_based_component")]
    public required QuantityBasedComponent QuantityBasedComponent { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
