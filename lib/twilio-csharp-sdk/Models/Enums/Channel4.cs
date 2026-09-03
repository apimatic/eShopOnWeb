using System.Text.Json.Serialization;
using Twilio.Core.Enum;

namespace Twilio.Models.Enums;

/// <summary>
/// Channel type. Required when participantId has multiple addresses or when using explicit address.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<Channel4>))]
public sealed record Channel4 : StringEnum<Channel4>
{
    private Channel4(string value) : base(value)
    {
    }

    public static readonly Channel4 Sms = new("SMS");

    public static readonly Channel4 Rcs = new("RCS");

    public static readonly Channel4 Whatsapp = new("WHATSAPP");

    public static readonly Channel4 Chat = new("CHAT");

    public static Channel4 FromValue(string value) => FromValueCore(value);
}
