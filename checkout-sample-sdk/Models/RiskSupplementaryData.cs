using System.Text.Json.Serialization;

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
}
