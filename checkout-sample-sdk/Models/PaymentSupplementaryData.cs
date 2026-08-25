using System.Text.Json.Serialization;

namespace PayPalServerSdk.Models;

/// <summary>
/// The supplementary data.
/// </summary>
public record PaymentSupplementaryData
{
    /// <summary>
    /// Identifiers related to a specific resource.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("related_ids")]
    public RelatedIdentifiers? RelatedIds { get; init; }
}
