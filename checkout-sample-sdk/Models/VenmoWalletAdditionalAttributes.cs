using System.Text.Json.Serialization;

namespace PayPalServerSdk.Models;

/// <summary>
/// Additional attributes associated with the use of this Venmo Wallet.
/// </summary>
public record VenmoWalletAdditionalAttributes
{
    /// <summary>
    /// The details about a customer in PayPal's system of record.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("customer")]
    public VenmoWalletCustomerInformation? Customer { get; init; }

    /// <summary>
    /// Resource consolidating common request and response attirbutes for vaulting Venmo Wallet.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("vault")]
    public VenmoWalletVaultAttributes? Vault { get; init; }
}
