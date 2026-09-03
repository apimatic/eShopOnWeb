using System.Text.Json.Serialization;
using Twilio.Core.Enum;

namespace Twilio.Models.Enums;

/// <summary>
/// Channel type for the Participant address.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<Channel12>))]
public sealed record Channel12 : StringEnum<Channel12>
{
    private Channel12(string value) : base(value)
    {
    }

    public static readonly Channel12 Voice = new("VOICE");

    public static readonly Channel12 Sms = new("SMS");

    public static readonly Channel12 Rcs = new("RCS");

    public static readonly Channel12 Whatsapp = new("WHATSAPP");

    public static readonly Channel12 Chat = new("CHAT");

    public static Channel12 FromValue(string value) => FromValueCore(value);
}
