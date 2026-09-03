using System.Text.Json.Serialization;
using Twilio.Core.Enum;

namespace Twilio.Models.Enums;

/// <summary>
/// For Messaging Services only: Include this parameter with a value of <c>fixed</c> in conjuction with the <c>send_time</c> parameter in order to <see href="https://www.twilio.com/docs/messaging/features/message-scheduling">schedule a Message</see>.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<MessageEnumScheduleType>))]
public sealed record MessageEnumScheduleType : StringEnum<MessageEnumScheduleType>
{
    private MessageEnumScheduleType(string value) : base(value)
    {
    }

    public static readonly MessageEnumScheduleType Fixed = new("fixed");

    public static MessageEnumScheduleType FromValue(string value) => FromValueCore(value);
}
