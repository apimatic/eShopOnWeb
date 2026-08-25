using System.Text.Json.Serialization;

namespace PayPalServerSdk.Models;

/// <summary>
/// The payment source used to fund the payment.
/// </summary>
public record PaymentSourceResponse
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
    /// Information used to pay Bancontact.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("bancontact")]
    public BancontactPaymentObject? Bancontact { get; init; }

    /// <summary>
    /// Information used to pay using BLIK.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("blik")]
    public BlikPaymentObject? Blik { get; init; }

    /// <summary>
    /// Information used to pay using eps.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("eps")]
    public EpsPaymentObject? Eps { get; init; }

    /// <summary>
    /// Information needed to pay using giropay.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("giropay")]
    public GiropayPaymentObject? Giropay { get; init; }

    /// <summary>
    /// Information used to pay using iDEAL.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("ideal")]
    public IDealPaymentObject? Ideal { get; init; }

    /// <summary>
    /// Information used to pay using MyBank.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("mybank")]
    public MyBankPaymentObject? Mybank { get; init; }

    /// <summary>
    /// Information used to pay using P24(Przelewy24).
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("p24")]
    public P24PaymentObject? P24 { get; init; }

    /// <summary>
    /// Information used to pay using Sofort.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("sofort")]
    public SofortPaymentObject? Sofort { get; init; }

    /// <summary>
    /// Information needed to pay using Trustly.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("trustly")]
    public TrustlyPaymentObject? Trustly { get; init; }

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
