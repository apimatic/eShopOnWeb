using System.Text.Json.Serialization;
using TwilioSdk.Core.Enum;

namespace TwilioSdk.Models.Enums;

[JsonConverter(typeof(StringEnumConverter<MessageEnumUpdateStatus>))]
public sealed record MessageEnumUpdateStatus : StringEnum<MessageEnumUpdateStatus>
{
    private MessageEnumUpdateStatus(string value) : base(value)
    {
    }

    public static readonly MessageEnumUpdateStatus Canceled = new("canceled");

    public static MessageEnumUpdateStatus FromValue(string value) => FromValueCore(value);
}
