using System.Text.Json.Serialization;
using MaxioAdvancedBilling.Core.Enum;

namespace MaxioAdvancedBilling.Models.Enums;

[JsonConverter(typeof(StringEnumConverter<ExpirationIntervalUnit>))]
public sealed record ExpirationIntervalUnit : StringEnum<ExpirationIntervalUnit>
{
    private ExpirationIntervalUnit(string value) : base(value)
    {
    }

    public static readonly ExpirationIntervalUnit Day = new("day");

    public static readonly ExpirationIntervalUnit Month = new("month");

    public static readonly ExpirationIntervalUnit Never = new("never");

    public static ExpirationIntervalUnit FromValue(string value) => FromValueCore(value);
}
