using System.Text.Json.Serialization;
using PayPal.Core.Models;
using PayPal.Models.Enums;

namespace PayPal.Models;

/// <summary>
/// Basic vault instruction specification that can be extended by specific payment sources that supports vaulting.
/// </summary>
public record VaultInstructionBase
{
    /// <summary>
    /// Defines how and when the payment source gets vaulted.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("store_in_vault")]
    public StoreInVaultInstruction? StoreInVault { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
