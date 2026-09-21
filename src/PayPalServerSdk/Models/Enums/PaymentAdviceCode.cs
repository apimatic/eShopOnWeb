using System.Text.Json.Serialization;
using PayPalServerSdk.Core.Enum;

namespace PayPalServerSdk.Models.Enums;

/// <summary>
/// The declined payment transactions might have payment advice codes. The card networks, like Visa and Mastercard, return payment advice codes.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<PaymentAdviceCode>))]
public sealed record PaymentAdviceCode : StringEnum<PaymentAdviceCode>
{
    private PaymentAdviceCode(string value) : base(value)
    {
    }

    /// <summary>
    /// For Mastercard, expired card account upgrade or portfolio sale conversion. Obtain new account information before next billing cycle.
    /// </summary>
    public static readonly PaymentAdviceCode _01 = new("01");

    /// <summary>
    /// For Mastercard, over credit limit or insufficient funds. Retry the transaction 72 hours later. For Visa, the card holder wants to stop only one specific payment in the recurring payment relationship. The merchant must NOT resubmit the same transaction. The merchant can continue the billing process in the subsequent billing period.
    /// </summary>
    public static readonly PaymentAdviceCode _02 = new("02");

    /// <summary>
    /// For Mastercard, account closed as fraudulent. Obtain another type of payment from customer due to account being closed or fraud. Possible reason: Account closed as fraudulent. For Visa, the card holder wants to stop all recurring payment transactions for a specific merchant. Stop recurring payment requests.
    /// </summary>
    public static readonly PaymentAdviceCode _03 = new("03");

    /// <summary>
    /// For Mastercard, token requirements not fulfilled for this token type.
    /// </summary>
    public static readonly PaymentAdviceCode _04 = new("04");

    /// <summary>
    /// For Mastercard, the card holder has been unsuccessful at canceling recurring payment through merchant. Stop recurring payment requests. For Visa, all recurring payments were canceled for the card number requested. Stop recurring payment requests.
    /// </summary>
    public static readonly PaymentAdviceCode _21 = new("21");

    /// <summary>
    /// For Mastercard, merchant does not qualify for product code.
    /// </summary>
    public static readonly PaymentAdviceCode _22 = new("22");

    /// <summary>
    /// For Mastercard, retry after 1 hour.
    /// </summary>
    public static readonly PaymentAdviceCode _24 = new("24");

    /// <summary>
    /// For Mastercard, retry after 24 hours.
    /// </summary>
    public static readonly PaymentAdviceCode _25 = new("25");

    /// <summary>
    /// For Mastercard, retry after 2 days.
    /// </summary>
    public static readonly PaymentAdviceCode _26 = new("26");

    /// <summary>
    /// For Mastercard, retry after 4 days.
    /// </summary>
    public static readonly PaymentAdviceCode _27 = new("27");

    /// <summary>
    /// For Mastercard, retry after 6 days.
    /// </summary>
    public static readonly PaymentAdviceCode _28 = new("28");

    /// <summary>
    /// For Mastercard, retry after 8 days.
    /// </summary>
    public static readonly PaymentAdviceCode _29 = new("29");

    /// <summary>
    /// For Mastercard, retry after 10 days .
    /// </summary>
    public static readonly PaymentAdviceCode _30 = new("30");

    /// <summary>
    /// For Mastercard, consumer non-reloadable prepaid card.
    /// </summary>
    public static readonly PaymentAdviceCode _40 = new("40");

    /// <summary>
    /// For Mastercard, consumer multi-use virtual card number.
    /// </summary>
    public static readonly PaymentAdviceCode _43 = new("43");

    public static PaymentAdviceCode FromValue(string value) => FromValueCore(value);
}
