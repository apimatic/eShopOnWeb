using System.Text.Json.Serialization;
using PayPalServerSdk.Core.Enum;

namespace PayPalServerSdk.Models.Enums;

/// <summary>
/// The interval at which the subscription is charged or billed.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<IntervalUnit>))]
public sealed record IntervalUnit : StringEnum<IntervalUnit>
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

    public static IntervalUnit FromValue(string value) => FromValueCore(value);
}
