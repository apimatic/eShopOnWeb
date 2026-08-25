using System.Text.Json.Serialization;

namespace PayPalServerSdk.Models;

/// <summary>
/// Supplementary data about a payment. This object passes information that can be used to improve risk assessments and processing costs, for example, by providing Level 2 and Level 3 payment data.
/// </summary>
public record SupplementaryData
{
    /// <summary>
    /// Merchants and partners can add Level 2 and 3 data to payments to reduce risk and payment processing costs. For more information about processing payments, see checkout or multiparty checkout.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("card")]
    public CardSupplementaryData? Card { get; init; }

    /// <summary>
    /// Additional information necessary to evaluate the risk profile of a transaction.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("risk")]
    public RiskSupplementaryData? Risk { get; init; }
}
