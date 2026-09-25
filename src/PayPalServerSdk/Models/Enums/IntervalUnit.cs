using System;
using System.Text.Json.Serialization;
using PayPalServerSdk.Core.Enum;

namespace PayPalServerSdk.Models.Enums;

/// <summary>
/// The interval at which the subscription is charged or billed.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<IntervalUnit>))]
public sealed record IntervalUnit : OpenStringEnum<IntervalUnit>
{
    private IntervalUnit(string value) : base(value)
    {
    }

    /// <summary>
    /// A daily billing cycle.
    /// </summary>
    public static readonly IntervalUnit Day = new("DAY");

    /// <summary>
    /// A weekly billing cycle.
    /// </summary>
    public static readonly IntervalUnit Week = new("WEEK");

    /// <summary>
    /// A monthly billing cycle.
    /// </summary>
    public static readonly IntervalUnit Month = new("MONTH");

    /// <summary>
    /// A yearly billing cycle.
    /// </summary>
    public static readonly IntervalUnit Year = new("YEAR");

    public TResult Match<TResult>(Func<TResult> onDay,
        Func<TResult> onWeek,
        Func<TResult> onMonth,
        Func<TResult> onYear,
        Func<string, TResult> otherwise) =>
        this switch
        {
            _ when this == Day => onDay(),
            _ when this == Week => onWeek(),
            _ when this == Month => onMonth(),
            _ when this == Year => onYear(),
            _ => otherwise(Value)
        };

    public void Match(Action onDay, Action onWeek, Action onMonth, Action onYear, Action<string> otherwise)
    {
        if (this == Day) onDay();
        else if (this == Week) onWeek();
        else if (this == Month) onMonth();
        else if (this == Year) onYear();
        else otherwise(Value);
    }
}
