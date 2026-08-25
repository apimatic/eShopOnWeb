using System.Text.Json.Serialization;

namespace PayPalServerSdk.Models;

/// <summary>
/// The payment method to vault with the instrument details.
/// </summary>
public record SetupTokenRequestPaymentSource
{
    /// <summary>
    /// A Resource representing a request to vault a Card.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("card")]
    public SetupTokenRequestCard? Card { get; init; }

    /// <summary>
    /// A resource representing a request to vault PayPal Wallet.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("paypal")]
    public VaultPayPalWalletRequest? Paypal { get; init; }

    /// <summary>
    /// A resource representing a request to vault Venmo.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("venmo")]
    public VaultVenmoRequest? Venmo { get; init; }

    /// <summary>
    /// A resource representing a request to vault Apple Pay.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("apple_pay")]
    public VaultApplePayRequest? ApplePay { get; init; }

    /// <summary>
    /// The Tokenized Payment Source representing a Request to Vault a Token.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("token")]
    public VaultTokenRequest? Token { get; init; }

    /// <summary>
    /// A Resource representing a request to vault a Bank used for ACH Debit.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("bank")]
    public BankRequest? Bank { get; init; }
}
