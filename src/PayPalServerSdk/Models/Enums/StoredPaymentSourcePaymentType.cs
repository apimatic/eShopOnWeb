using System;
using System.Text.Json.Serialization;
using PayPalServerSdk.Core.Enum;

namespace PayPalServerSdk.Models.Enums;

/// <summary>
/// Indicates the type of the stored payment_source payment.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<StoredPaymentSourcePaymentType>))]
public sealed record StoredPaymentSourcePaymentType : OpenStringEnum<StoredPaymentSourcePaymentType>
{
    private StoredPaymentSourcePaymentType(string value) : base(value)
    {
    }

    /// <summary>
    /// One Time payment such as online purchase or donation. (e.g. Checkout with one-click).
    /// </summary>
    public static readonly StoredPaymentSourcePaymentType OneTime = new("ONE_TIME");

    /// <summary>
    /// Payment which is part of a series of payments with fixed or variable amounts, following a fixed time interval. (e.g. Subscription payments).
    /// </summary>
    public static readonly StoredPaymentSourcePaymentType Recurring = new("RECURRING");

    /// <summary>
    /// Payment which is part of a series of payments that occur on a non-fixed schedule and/or have variable amounts. (e.g. Account Topup payments).
    /// </summary>
    public static readonly StoredPaymentSourcePaymentType Unscheduled = new("UNSCHEDULED");

    public TResult Match<TResult>(Func<TResult> onOneTime,
        Func<TResult> onRecurring,
        Func<TResult> onUnscheduled,
        Func<string, TResult> otherwise) =>
        this switch
        {
            _ when this == OneTime => onOneTime(),
            _ when this == Recurring => onRecurring(),
            _ when this == Unscheduled => onUnscheduled(),
            _ => otherwise(Value)
        };

    public void Match(Action onOneTime, Action onRecurring, Action onUnscheduled, Action<string> otherwise)
    {
        if (this == OneTime) onOneTime();
        else if (this == Recurring) onRecurring();
        else if (this == Unscheduled) onUnscheduled();
        else otherwise(Value);
    }
}
