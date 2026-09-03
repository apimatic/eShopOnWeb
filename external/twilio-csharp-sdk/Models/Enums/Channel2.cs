using System.Text.Json.Serialization;
using Twilio.Core.Enum;

namespace Twilio.Models.Enums;

/// <summary>
/// The channel for Communication.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<Channel2>))]
public sealed record Channel2 : StringEnum<Channel2>
{
    private Channel2(string value) : base(value)
    {
    }

    public static readonly Channel2 Voice = new("VOICE");

    public static readonly Channel2 Sms = new("SMS");

    public static readonly Channel2 Rcs = new("RCS");

    public static readonly Channel2 Whatsapp = new("WHATSAPP");

    public static readonly Channel2 Chat = new("CHAT");

    public static Channel2 FromValue(string value) => FromValueCore(value);
}
