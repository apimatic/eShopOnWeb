using System.Text.Json.Serialization;
using MaxioAdvancedBilling.Core.Enum;

namespace MaxioAdvancedBilling.Models.Enums;

[JsonConverter(typeof(StringEnumConverter<Direction>))]
public sealed record Direction : StringEnum<Direction>
{
    private Direction(string value) : base(value)
    {
    }

    public static readonly Direction Asc = new("asc");

    public static readonly Direction Desc = new("desc");

    public static Direction FromValue(string value) => FromValueCore(value);
}
