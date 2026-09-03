using System.Text.Json.Serialization;
using Twilio.Core.Enum;

namespace Twilio.Models.Enums;

[JsonConverter(typeof(StringEnumConverter<SmsMessageEnumDirection>))]
public sealed record SmsMessageEnumDirection : StringEnum<SmsMessageEnumDirection>
{
    private SmsMessageEnumDirection(string value) : base(value)
    {
    }

    public static readonly SmsMessageEnumDirection Inbound = new("inbound");

    public static readonly SmsMessageEnumDirection OutboundApi = new("outbound-api");

    public static readonly SmsMessageEnumDirection OutboundCall = new("outbound-call");

    public static readonly SmsMessageEnumDirection OutboundReply = new("outbound-reply");

    public static SmsMessageEnumDirection FromValue(string value) => FromValueCore(value);
}
