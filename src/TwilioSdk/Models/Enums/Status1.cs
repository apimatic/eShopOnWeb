using System.Text.Json.Serialization;
using TwilioSdk.Core.Enum;

namespace TwilioSdk.Models.Enums;

[JsonConverter(typeof(StringEnumConverter<Status1>))]
public sealed record Status1 : StringEnum<Status1>
{
    private Status1(string value) : base(value)
    {
    }

    public static readonly Status1 Live = new("LIVE");

    public static readonly Status1 NotLive = new("NOT_LIVE");

    public static Status1 FromValue(string value) => FromValueCore(value);
}
