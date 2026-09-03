using System.Text.Json.Serialization;
using Twilio.Core.Enum;

namespace Twilio.Models.Enums;

[JsonConverter(typeof(StringEnumConverter<Op>))]
public sealed record Op : StringEnum<Op>
{
    private Op(string value) : base(value)
    {
    }

    public static readonly Op And = new("AND");

    public static readonly Op Or = new("OR");

    public static readonly Op Eq = new("EQ");

    public static readonly Op Ne = new("NE");

    public static readonly Op Gt = new("GT");

    public static readonly Op Lt = new("LT");

    public static readonly Op In = new("IN");

    public static Op FromValue(string value) => FromValueCore(value);
}
