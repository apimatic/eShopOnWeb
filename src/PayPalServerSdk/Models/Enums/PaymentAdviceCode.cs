using System;
using System.Text.Json.Serialization;
using PayPalServerSdk.Core.Enum;

namespace PayPalServerSdk.Models.Enums;

/// <summary>
/// The declined payment transactions might have payment advice codes. The card networks, like Visa and Mastercard, return payment advice codes.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<PaymentAdviceCode>))]
public sealed record PaymentAdviceCode : OpenStringEnum<PaymentAdviceCode>
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

    public TResult Match<TResult>(Func<TResult> on_01,
        Func<TResult> on_02,
        Func<TResult> on_03,
        Func<TResult> on_04,
        Func<TResult> on_21,
        Func<TResult> on_22,
        Func<TResult> on_24,
        Func<TResult> on_25,
        Func<TResult> on_26,
        Func<TResult> on_27,
        Func<TResult> on_28,
        Func<TResult> on_29,
        Func<TResult> on_30,
        Func<TResult> on_40,
        Func<TResult> on_43,
        Func<string, TResult> otherwise) =>
        this switch
        {
            _ when this == _01 => on_01(),
            _ when this == _02 => on_02(),
            _ when this == _03 => on_03(),
            _ when this == _04 => on_04(),
            _ when this == _21 => on_21(),
            _ when this == _22 => on_22(),
            _ when this == _24 => on_24(),
            _ when this == _25 => on_25(),
            _ when this == _26 => on_26(),
            _ when this == _27 => on_27(),
            _ when this == _28 => on_28(),
            _ when this == _29 => on_29(),
            _ when this == _30 => on_30(),
            _ when this == _40 => on_40(),
            _ when this == _43 => on_43(),
            _ => otherwise(Value)
        };

    public void Match(Action on_01,
        Action on_02,
        Action on_03,
        Action on_04,
        Action on_21,
        Action on_22,
        Action on_24,
        Action on_25,
        Action on_26,
        Action on_27,
        Action on_28,
        Action on_29,
        Action on_30,
        Action on_40,
        Action on_43,
        Action<string> otherwise)
    {
        if (this == _01) on_01();
        else if (this == _02) on_02();
        else if (this == _03) on_03();
        else if (this == _04) on_04();
        else if (this == _21) on_21();
        else if (this == _22) on_22();
        else if (this == _24) on_24();
        else if (this == _25) on_25();
        else if (this == _26) on_26();
        else if (this == _27) on_27();
        else if (this == _28) on_28();
        else if (this == _29) on_29();
        else if (this == _30) on_30();
        else if (this == _40) on_40();
        else if (this == _43) on_43();
        else otherwise(Value);
    }
}
