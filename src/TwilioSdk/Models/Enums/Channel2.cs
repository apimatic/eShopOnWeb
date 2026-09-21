using System.Text.Json.Serialization;
using TwilioSdk.Core.Enum;

namespace TwilioSdk.Models.Enums;

/// <summary>
/// Channel type for sending communications. Note: VOICE is receive-only and not supported for send operations.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<Channel2>))]
public sealed record Channel2 : StringEnum<Channel2>
{
    private Channel2(string value) : base(value)
    {
    }

    public static readonly Channel2 Sms = new("SMS");

    public static readonly Channel2 Rcs = new("RCS");

    public static readonly Channel2 Whatsapp = new("WHATSAPP");

    public static readonly Channel2 Chat = new("CHAT");

    public static Channel2 FromValue(string value) => FromValueCore(value);
}
