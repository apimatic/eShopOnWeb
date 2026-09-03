using System.Text.Json.Serialization;
using Twilio.Core.Enum;

namespace Twilio.Models.Enums;

/// <summary>
/// Purpose of SMS messages
/// </summary>
[JsonConverter(typeof(StringEnumConverter<MessagePurpose>))]
public sealed record MessagePurpose : StringEnum<MessagePurpose>
{
    private MessagePurpose(string value) : base(value)
    {
    }

    public static readonly MessagePurpose Transactional = new("TRANSACTIONAL");

    public static readonly MessagePurpose Promotional = new("PROMOTIONAL");

    public static MessagePurpose FromValue(string value) => FromValueCore(value);
}
