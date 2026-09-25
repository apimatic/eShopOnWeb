using System;
using System.Text.Json.Serialization;
using PayPalServerSdk.Core.Enum;

namespace PayPalServerSdk.Models.Enums;

/// <summary>
/// The reason code for the payment failure.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<ReasonCode>))]
public sealed record ReasonCode : OpenStringEnum<ReasonCode>
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

    public TResult Match<TResult>(Func<TResult> onPaymentDenied,
        Func<TResult> onInternalServerError,
        Func<TResult> onPayeeAccountRestricted,
        Func<TResult> onPayerAccountRestricted,
        Func<TResult> onPayerCannotPay,
        Func<TResult> onSendingLimitExceeded,
        Func<TResult> onTransactionReceivingLimitExceeded,
        Func<TResult> onCurrencyMismatch,
        Func<string, TResult> otherwise) =>
        this switch
        {
            _ when this == PaymentDenied => onPaymentDenied(),
            _ when this == InternalServerError => onInternalServerError(),
            _ when this == PayeeAccountRestricted => onPayeeAccountRestricted(),
            _ when this == PayerAccountRestricted => onPayerAccountRestricted(),
            _ when this == PayerCannotPay => onPayerCannotPay(),
            _ when this == SendingLimitExceeded => onSendingLimitExceeded(),
            _ when this == TransactionReceivingLimitExceeded => onTransactionReceivingLimitExceeded(),
            _ when this == CurrencyMismatch => onCurrencyMismatch(),
            _ => otherwise(Value)
        };

    public void Match(Action onPaymentDenied,
        Action onInternalServerError,
        Action onPayeeAccountRestricted,
        Action onPayerAccountRestricted,
        Action onPayerCannotPay,
        Action onSendingLimitExceeded,
        Action onTransactionReceivingLimitExceeded,
        Action onCurrencyMismatch,
        Action<string> otherwise)
    {
        if (this == PaymentDenied) onPaymentDenied();
        else if (this == InternalServerError) onInternalServerError();
        else if (this == PayeeAccountRestricted) onPayeeAccountRestricted();
        else if (this == PayerAccountRestricted) onPayerAccountRestricted();
        else if (this == PayerCannotPay) onPayerCannotPay();
        else if (this == SendingLimitExceeded) onSendingLimitExceeded();
        else if (this == TransactionReceivingLimitExceeded) onTransactionReceivingLimitExceeded();
        else if (this == CurrencyMismatch) onCurrencyMismatch();
        else otherwise(Value);
    }
}
