using System.Text.Json.Serialization;
using Twilio.Core.Enum;

namespace Twilio.Models.Enums;

[JsonConverter(typeof(StringEnumConverter<MessageEnumTrafficType>))]
public sealed record MessageEnumTrafficType : StringEnum<MessageEnumTrafficType>
{
    private MessageEnumTrafficType(string value) : base(value)
    {
    }

    public static readonly MessageEnumTrafficType Free = new("free");

    public static MessageEnumTrafficType FromValue(string value) => FromValueCore(value);
}
