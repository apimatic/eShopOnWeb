using System.Text.Json.Serialization;
using MaxioAdvancedBilling.Core.Models;

namespace MaxioAdvancedBilling.Models;

public record PaymentCollectionMethodChanged
{
    [JsonPropertyName("previous_value")]
    public required string PreviousValue { get; init; }

    [JsonPropertyName("current_value")]
    public required string CurrentValue { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
