using System.Text.Json.Serialization;
using Twilio.Core.Enum;

namespace Twilio.Models.Enums;

[JsonConverter(typeof(StringEnumConverter<Channel5>))]
public sealed record Channel5 : StringEnum<Channel5>
{
    private Channel5(string value) : base(value)
    {
    }

    public static readonly Channel5 Voice = new("VOICE");

    public static readonly Channel5 Sms = new("SMS");

    public static readonly Channel5 Rcs = new("RCS");

    public static readonly Channel5 Email = new("EMAIL");

    public static readonly Channel5 Whatsapp = new("WHATSAPP");

    public static readonly Channel5 Chat = new("CHAT");

    public static readonly Channel5 Api = new("API");

    public static readonly Channel5 System = new("SYSTEM");

    public static Channel5 FromValue(string value) => FromValueCore(value);
}
