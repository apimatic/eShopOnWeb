using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace PayPalServerSdk.Models;

/// <summary>
/// Additional attributes associated with the use of a PayPal Wallet.
/// </summary>
public record PayPalWalletAttributesResponse
{
    /// <summary>
    /// The details about a saved PayPal Wallet payment source.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("vault")]
    public PayPalWalletVaultResponse? Vault { get; init; }

    /// <summary>
    /// An array of merchant cobranded cards used by buyer to complete an order. This array will be present if a merchant has onboarded their cobranded card with PayPal and provided corresponding label(s).
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("cobranded_cards")]
    [MinLength(0)]
    [MaxLength(25)]
    public IReadOnlyList<CobrandedCard>? CobrandedCards { get; init; }
}
