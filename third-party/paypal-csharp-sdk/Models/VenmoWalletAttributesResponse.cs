using System.Text.Json.Serialization;
using PayPal.Core.Models;

namespace PayPal.Models;

/// <summary>
/// Additional attributes associated with the use of a Venmo Wallet.
/// </summary>
public record VenmoWalletAttributesResponse
{
    /// <summary>
    /// The details about a saved venmo payment source.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("vault")]
    public VenmoVaultResponse? Vault { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
