using System.Text.Json.Serialization;
using MaxioAdvancedBilling.Core.Models;

namespace MaxioAdvancedBilling.Models;

public record CloneComponentPricePointRequest
{
    [JsonPropertyName("price_point")]
    public required CloneComponentPricePoint PricePoint { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
