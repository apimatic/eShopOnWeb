using System.Text.Json.Serialization;

namespace PayPalServerSdk.Models;

/// <summary>
/// The payment source definition.
/// </summary>
public record PaymentSource
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
    /// Information needed to pay using Bancontact.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("bancontact")]
    public BancontactPaymentRequest? Bancontact { get; init; }

    /// <summary>
    /// Information needed to pay using BLIK.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("blik")]
    public BlikPaymentRequest? Blik { get; init; }

    /// <summary>
    /// Information needed to pay using eps.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("eps")]
    public EpsPaymentRequest? Eps { get; init; }

    /// <summary>
    /// Information needed to pay using giropay.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("giropay")]
    public GiropayPaymentRequest? Giropay { get; init; }

    /// <summary>
    /// Information needed to pay using iDEAL.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("ideal")]
    public IDealPaymentRequest? Ideal { get; init; }

    /// <summary>
    /// Information needed to pay using MyBank.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("mybank")]
    public MyBankPaymentRequest? Mybank { get; init; }

    /// <summary>
    /// Information needed to pay using P24 (Przelewy24).
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("p24")]
    public P24PaymentRequest? P24 { get; init; }

    /// <summary>
    /// Information needed to pay using Sofort.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("sofort")]
    public SofortPaymentRequest? Sofort { get; init; }

    /// <summary>
    /// Information needed to pay using Trustly.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("trustly")]
    public TrustlyPaymentRequest? Trustly { get; init; }

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
