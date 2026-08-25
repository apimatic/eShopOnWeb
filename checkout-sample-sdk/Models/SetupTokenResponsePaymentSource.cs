using System.Text.Json.Serialization;

namespace PayPalServerSdk.Models;

/// <summary>
/// The setup payment method details.
/// </summary>
public record SetupTokenResponsePaymentSource
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("card")]
    public SetupTokenResponseCard? Card { get; init; }

    /// <summary>
    /// Full representation of a PayPal Payment Token.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("paypal")]
    public PayPalPaymentToken? Paypal { get; init; }

    /// <summary>
    /// Full representation of a Venmo Payment Token.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("venmo")]
    public VenmoPaymentToken? Venmo { get; init; }
}
