using System.Text.Json.Serialization;
using TwilioSdk.Core.Enum;

namespace TwilioSdk.Models.Enums;

/// <summary>
/// Channel type for the Participant address.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<Channel11>))]
public sealed record Channel11 : StringEnum<Channel11>
{
    private Channel11(string value) : base(value)
    {
    }

    public static readonly Channel11 Voice = new("VOICE");

    public static readonly Channel11 Sms = new("SMS");

    public static readonly Channel11 Rcs = new("RCS");

    public static readonly Channel11 Whatsapp = new("WHATSAPP");

    public static readonly Channel11 Chat = new("CHAT");

    public static Channel11 FromValue(string value) => FromValueCore(value);
}
