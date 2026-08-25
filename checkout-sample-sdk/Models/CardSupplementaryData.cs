using System.Text.Json.Serialization;

namespace PayPalServerSdk.Models;

/// <summary>
/// Merchants and partners can add Level 2 and 3 data to payments to reduce risk and payment processing costs. For more information about processing payments, see checkout or multiparty checkout.
/// </summary>
public record CardSupplementaryData
{
    /// <summary>
    /// The level 2 card processing data collections. If your merchant account has been configured for Level 2 processing this field will be passed to the processor on your behalf. Please contact your PayPal Technical Account Manager to define level 2 data for your business.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("level_2")]
    public Level2CardProcessingData? Level2 { get; init; }

    /// <summary>
    /// The level 3 card processing data collections, If your merchant account has been configured for Level 3 processing this field will be passed to the processor on your behalf. Please contact your PayPal Technical Account Manager to define level 3 data for your business.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("level_3")]
    public Level3CardProcessingData? Level3 { get; init; }
}
