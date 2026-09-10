using System.Text.Json.Serialization;
using MaxioAdvancedBilling.Core.Enum;

namespace MaxioAdvancedBilling.Models.Enums;

[JsonConverter(typeof(StringEnumConverter<IntervalUnit>))]
public sealed record IntervalUnit : StringEnum<IntervalUnit>
{
    private IntervalUnit(string value) : base(value)
    {
    }

    public static readonly IntervalUnit Day = new("day");

    public static readonly IntervalUnit Month = new("month");

    public static IntervalUnit FromValue(string value) => FromValueCore(value);
}
