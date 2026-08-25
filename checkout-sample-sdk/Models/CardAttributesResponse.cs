using System.Text.Json.Serialization;

namespace PayPalServerSdk.Models;

/// <summary>
/// Additional attributes associated with the use of this card.
/// </summary>
public record CardAttributesResponse
{
    /// <summary>
    /// The details about a saved Card payment source.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("vault")]
    public CardVaultResponse? Vault { get; init; }
}
