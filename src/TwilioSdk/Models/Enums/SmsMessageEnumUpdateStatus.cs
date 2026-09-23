using System.Text.Json.Serialization;
using TwilioSdk.Core.Enum;

namespace TwilioSdk.Models.Enums;

[JsonConverter(typeof(StringEnumConverter<SmsMessageEnumUpdateStatus>))]
public sealed record SmsMessageEnumUpdateStatus : StringEnum<SmsMessageEnumUpdateStatus>
{
    private SmsMessageEnumUpdateStatus(string value) : base(value)
    {
    }

    public static readonly SmsMessageEnumUpdateStatus Canceled = new("canceled");

    public static SmsMessageEnumUpdateStatus FromValue(string value) => FromValueCore(value);
}
