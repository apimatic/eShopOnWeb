using System.Text.Json.Serialization;
using MaxioAdvancedBilling.Core.Models;

namespace MaxioAdvancedBilling.Models;

public record CreatePrepaidComponent
{
    [JsonPropertyName("prepaid_usage_component")]
    public required PrepaidUsageComponent PrepaidUsageComponent { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
