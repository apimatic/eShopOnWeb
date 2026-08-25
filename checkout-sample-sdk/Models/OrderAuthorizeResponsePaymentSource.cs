using System.Text.Json.Serialization;

namespace PayPalServerSdk.Models;

/// <summary>
/// The payment source used to fund the payment.
/// </summary>
public record OrderAuthorizeResponsePaymentSource
{
    /// <summary>
    /// The payment card to use to fund a payment. Card can be a credit or debit card.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("card")]
    public CardResponse? Card { get; init; }

    /// <summary>
    /// The PayPal Wallet response.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("paypal")]
    public PayPalWalletResponse? Paypal { get; init; }

    /// <summary>
    /// Information needed to pay using ApplePay.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("apple_pay")]
    public ApplePayPaymentObject? ApplePay { get; init; }

    /// <summary>
    /// Google Pay Wallet payment data.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("google_pay")]
    public GooglePayWalletResponse? GooglePay { get; init; }

    /// <summary>
    /// Venmo wallet response.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("venmo")]
    public VenmoWalletResponse? Venmo { get; init; }
}
