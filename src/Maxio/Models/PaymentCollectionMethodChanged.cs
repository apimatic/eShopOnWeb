using System.Text.Json.Serialization;
using Maxio.Core.Models;

namespace Maxio.Models;

public record PaymentCollectionMethodChanged
{
    [JsonPropertyName("previous_value")]
    public required string PreviousValue { get; init; }

    [JsonPropertyName("current_value")]
    public required string CurrentValue { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
