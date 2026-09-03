using System.Text.Json.Serialization;
using Twilio.Core.Enum;

namespace Twilio.Models.Enums;

[JsonConverter(typeof(StringEnumConverter<SmsMessageEnumUpdateStatus>))]
public sealed record SmsMessageEnumUpdateStatus : StringEnum<SmsMessageEnumUpdateStatus>
{
    private SmsMessageEnumUpdateStatus(string value) : base(value)
    {
    }

    public static readonly SmsMessageEnumUpdateStatus Canceled = new("canceled");

    public static SmsMessageEnumUpdateStatus FromValue(string value) => FromValueCore(value);
}
