using System.Text.Json.Serialization;
using Twilio.Core.Enum;

namespace Twilio.Models.Enums;

[JsonConverter(typeof(StringEnumConverter<Channel6>))]
public sealed record Channel6 : StringEnum<Channel6>
{
    private Channel6(string value) : base(value)
    {
    }

    public static readonly Channel6 Voice = new("VOICE");

    public static readonly Channel6 Sms = new("SMS");

    public static readonly Channel6 Rcs = new("RCS");

    public static readonly Channel6 Whatsapp = new("WHATSAPP");

    public static readonly Channel6 Chat = new("CHAT");

    public static Channel6 FromValue(string value) => FromValueCore(value);
}
