using System.Text.Json.Serialization;
using Twilio.Core.Enum;

namespace Twilio.Models.Enums;

/// <summary>
/// The direction of the message. Can be: <c>inbound</c> for incoming messages, <c>outbound-api</c> for messages created by the REST API, <c>outbound-call</c> for messages created during a call, or <c>outbound-reply</c> for messages created in response to an incoming message.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<MessageEnumDirection>))]
public sealed record MessageEnumDirection : StringEnum<MessageEnumDirection>
{
    private MessageEnumDirection(string value) : base(value)
    {
    }

    public static readonly MessageEnumDirection Inbound = new("inbound");

    public static readonly MessageEnumDirection OutboundApi = new("outbound-api");

    public static readonly MessageEnumDirection OutboundCall = new("outbound-call");

    public static readonly MessageEnumDirection OutboundReply = new("outbound-reply");

    public static MessageEnumDirection FromValue(string value) => FromValueCore(value);
}
