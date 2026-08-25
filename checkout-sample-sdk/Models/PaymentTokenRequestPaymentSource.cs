using System.Text.Json.Serialization;

namespace PayPalServerSdk.Models;

/// <summary>
/// The payment method to vault with the instrument details.
/// </summary>
public record PaymentTokenRequestPaymentSource
{
    /// <summary>
    /// A Resource representing a request to vault a Card.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("card")]
    public PaymentTokenRequestCard? Card { get; init; }

    /// <summary>
    /// The Tokenized Payment Source representing a Request to Vault a Token.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("token")]
    public VaultTokenRequest? Token { get; init; }
}
