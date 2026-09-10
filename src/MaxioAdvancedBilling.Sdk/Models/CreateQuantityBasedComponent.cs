using System.Text.Json.Serialization;
using MaxioAdvancedBilling.Core.Models;

namespace MaxioAdvancedBilling.Models;

public record CreateQuantityBasedComponent
{
    [JsonPropertyName("quantity_based_component")]
    public required QuantityBasedComponent QuantityBasedComponent { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
