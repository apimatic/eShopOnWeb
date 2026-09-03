using System.Text.Json.Serialization;
using Twilio.Core.Enum;

namespace Twilio.Models.Enums;

/// <summary>
/// The messaging channel. Must be "APPLE".
/// </summary>
[JsonConverter(typeof(StringEnumConverter<Channel11>))]
public sealed record Channel11 : StringEnum<Channel11>
{
    private Channel11(string value) : base(value)
    {
    }

    public static readonly Channel11 Apple = new("APPLE");

    public static Channel11 FromValue(string value) => FromValueCore(value);
}
