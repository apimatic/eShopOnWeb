using System.Text.Json.Serialization;
using TwilioSdk.Core.Enum;

namespace TwilioSdk.Models.Enums;

/// <summary>
/// The channel for Communication.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<Channel1>))]
public sealed record Channel1 : StringEnum<Channel1>
{
    private Channel1(string value) : base(value)
    {
    }

    public static readonly Channel1 Voice = new("VOICE");

    public static readonly Channel1 Sms = new("SMS");

    public static readonly Channel1 Rcs = new("RCS");

    public static readonly Channel1 Whatsapp = new("WHATSAPP");

    public static readonly Channel1 Chat = new("CHAT");

    public static Channel1 FromValue(string value) => FromValueCore(value);
}
