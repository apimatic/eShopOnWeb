using System.Text.Json.Serialization;
using PayPal.Core.Models;

namespace PayPal.Models;

/// <summary>
/// Additional attributes associated with the use of this PayPal Wallet.
/// </summary>
public record PayPalWalletAttributes
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("customer")]
    public PayPalWalletCustomerRequest? Customer { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("vault")]
    public PayPalWalletVaultInstruction? Vault { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
