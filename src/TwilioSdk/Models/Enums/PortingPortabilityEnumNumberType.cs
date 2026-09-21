using System.Text.Json.Serialization;
using TwilioSdk.Core.Enum;

namespace TwilioSdk.Models.Enums;

/// <summary>
/// The type of the requested phone number. One of <c>LOCAL</c>, <c>UNKNOWN</c>, <c>MOBILE</c>, <c>TOLL-FREE</c>.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<PortingPortabilityEnumNumberType>))]
public sealed record PortingPortabilityEnumNumberType : StringEnum<PortingPortabilityEnumNumberType>
{
    private PortingPortabilityEnumNumberType(string value) : base(value)
    {
    }

    public static readonly PortingPortabilityEnumNumberType Local = new("LOCAL");

    public static readonly PortingPortabilityEnumNumberType Unknown = new("UNKNOWN");

    public static readonly PortingPortabilityEnumNumberType Mobile = new("MOBILE");

    public static readonly PortingPortabilityEnumNumberType TollFree = new("TOLL-FREE");

    public static PortingPortabilityEnumNumberType FromValue(string value) => FromValueCore(value);
}
