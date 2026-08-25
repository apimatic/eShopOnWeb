using System.Text.Json.Serialization;

namespace PayPalServerSdk.Models;

/// <summary>
/// Additional attributes associated with the use of this card.
/// </summary>
public record CardAttributes
{
    /// <summary>
    /// The details about a customer in PayPal's system of record.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("customer")]
    public CardCustomerInformation? Customer { get; init; }

    /// <summary>
    /// Basic vault instruction specification that can be extended by specific payment sources that supports vaulting.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("vault")]
    public VaultInstructionBase? Vault { get; init; }

    /// <summary>
    /// The API caller can opt in to verify the card through PayPal offered verification services (e.g. Smart Dollar Auth, 3DS).
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("verification")]
    public CardVerification? Verification { get; init; }
}
