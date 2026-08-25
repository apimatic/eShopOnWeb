using System.Text.Json.Serialization;
using PayPalServerSdk.Models.Enums;

namespace PayPalServerSdk.Models;

/// <summary>
/// Base vaulting specification. The object can be extended for specific use cases within each payment_source that supports vaulting.
/// </summary>
public record VaultInstruction
{
    /// <summary>
    /// Defines how and when the payment source gets vaulted.
    /// </summary>
    [JsonPropertyName("store_in_vault")]
    public required StoreInVaultInstruction StoreInVault { get; init; }
}
