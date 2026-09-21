using System.Text.Json.Serialization;
using PayPalServerSdk.Core.Enum;

namespace PayPalServerSdk.Models.Enums;

/// <summary>
/// The reason code for the payment failure.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<ReasonCode>))]
public sealed record ReasonCode : StringEnum<ReasonCode>
{
    private ReasonCode(string value) : base(value)
    {
    }

    /// <summary>
    /// PayPal declined the payment due to one or more customer issues.
    /// </summary>
    public static readonly ReasonCode PaymentDenied = new("PAYMENT_DENIED");

    /// <summary>
    /// An internal server error has occurred.
    /// </summary>
    public static readonly ReasonCode InternalServerError = new("INTERNAL_SERVER_ERROR");

    /// <summary>
    /// The payee account is not in good standing and cannot receive payments.
    /// </summary>
    public static readonly ReasonCode PayeeAccountRestricted = new("PAYEE_ACCOUNT_RESTRICTED");

    /// <summary>
    /// The payer account is not in good standing and cannot make payments.
    /// </summary>
    public static readonly ReasonCode PayerAccountRestricted = new("PAYER_ACCOUNT_RESTRICTED");

    /// <summary>
    /// Payer cannot pay for this transaction.
    /// </summary>
    public static readonly ReasonCode PayerCannotPay = new("PAYER_CANNOT_PAY");

    /// <summary>
    /// The transaction exceeds the payer's sending limit.
    /// </summary>
    public static readonly ReasonCode SendingLimitExceeded = new("SENDING_LIMIT_EXCEEDED");

    /// <summary>
    /// The transaction exceeds the receiver's receiving limit.
    /// </summary>
    public static readonly ReasonCode TransactionReceivingLimitExceeded = new("TRANSACTION_RECEIVING_LIMIT_EXCEEDED");

    /// <summary>
    /// The transaction is declined due to a currency mismatch.
    /// </summary>
    public static readonly ReasonCode CurrencyMismatch = new("CURRENCY_MISMATCH");

    public static ReasonCode FromValue(string value) => FromValueCore(value);
}
