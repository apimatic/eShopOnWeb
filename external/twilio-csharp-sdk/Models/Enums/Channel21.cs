using System.Text.Json.Serialization;
using Twilio.Core.Enum;

namespace Twilio.Models.Enums;

/// <summary>
/// Channel type for sending communications. Note: VOICE is receive-only and not supported for send operations.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<Channel21>))]
public sealed record Channel21 : StringEnum<Channel21>
{
    private Channel21(string value) : base(value)
    {
    }

    public static readonly Channel21 Sms = new("SMS");

    public static readonly Channel21 Rcs = new("RCS");

    public static readonly Channel21 Whatsapp = new("WHATSAPP");

    public static readonly Channel21 Chat = new("CHAT");

    public static Channel21 FromValue(string value) => FromValueCore(value);
}
