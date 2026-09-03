using System.Text.Json.Serialization;
using TwilioSdk.Core.Enum;

namespace TwilioSdk.Models.Enums;

/// <summary>
/// Channel type for address resolution.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<Channel3>))]
public sealed record Channel3 : StringEnum<Channel3>
{
    private Channel3(string value) : base(value)
    {
    }

    public static readonly Channel3 Voice = new("VOICE");

    public static readonly Channel3 Sms = new("SMS");

    public static readonly Channel3 Rcs = new("RCS");

    public static readonly Channel3 Whatsapp = new("WHATSAPP");

    public static readonly Channel3 Chat = new("CHAT");

    public static Channel3 FromValue(string value) => FromValueCore(value);
}
