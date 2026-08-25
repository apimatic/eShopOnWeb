using System.Text.Json.Serialization;
using PayPalServerSdk.Models.Enums;

namespace PayPalServerSdk.Models;

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
}
