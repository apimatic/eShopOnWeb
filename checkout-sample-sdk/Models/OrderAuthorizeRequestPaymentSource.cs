using System.Text.Json.Serialization;

namespace PayPalServerSdk.Models;

/// <summary>
/// The payment source definition.
/// </summary>
public record OrderAuthorizeRequestPaymentSource
{
    /// <summary>
    /// The payment card to use to fund a payment. Can be a credit or debit card. Note: Passing card number, cvv and expiry directly via the API requires PCI SAQ D compliance. *PayPal offers a mechanism by which you do not have to take on the PCI SAQ D burden by using hosted fields - refer to this Integration Guide*.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("card")]
    public CardRequest? Card { get; init; }

    /// <summary>
    /// The tokenized payment source to fund a payment.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("token")]
    public Token? Token { get; init; }

    /// <summary>
    /// A resource that identifies a PayPal Wallet is used for payment.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("paypal")]
    public PayPalWallet? Paypal { get; init; }

    /// <summary>
    /// Information needed to pay using ApplePay.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("apple_pay")]
    public ApplePayRequest? ApplePay { get; init; }

    /// <summary>
    /// Information needed to pay using Google Pay.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("google_pay")]
    public GooglePayRequest? GooglePay { get; init; }

    /// <summary>
    /// Information needed to pay using Venmo.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("venmo")]
    public VenmoWalletRequest? Venmo { get; init; }
}
