using System.Text.Json.Serialization;
using PayPalServerSdk.Core.Models;

namespace PayPalServerSdk.Models;

/// <summary>
/// Additional information necessary to evaluate the risk profile of a transaction.
/// </summary>
public record RiskSupplementaryData
{
    /// <summary>
    /// Profile information of the sender or receiver.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("customer")]
    public ParticipantMetadata? Customer { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
