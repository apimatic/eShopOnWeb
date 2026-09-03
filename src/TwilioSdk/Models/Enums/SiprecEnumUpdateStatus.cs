using System.Text.Json.Serialization;
using TwilioSdk.Core.Enum;

namespace TwilioSdk.Models.Enums;

[JsonConverter(typeof(StringEnumConverter<SiprecEnumUpdateStatus>))]
public sealed record SiprecEnumUpdateStatus : StringEnum<SiprecEnumUpdateStatus>
{
    private SiprecEnumUpdateStatus(string value) : base(value)
    {
    }

    public static readonly SiprecEnumUpdateStatus Stopped = new("stopped");

    public static SiprecEnumUpdateStatus FromValue(string value) => FromValueCore(value);
}
