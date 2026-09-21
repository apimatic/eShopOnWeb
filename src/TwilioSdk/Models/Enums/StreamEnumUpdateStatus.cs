using System.Text.Json.Serialization;
using TwilioSdk.Core.Enum;

namespace TwilioSdk.Models.Enums;

[JsonConverter(typeof(StringEnumConverter<StreamEnumUpdateStatus>))]
public sealed record StreamEnumUpdateStatus : StringEnum<StreamEnumUpdateStatus>
{
    private StreamEnumUpdateStatus(string value) : base(value)
    {
    }

    public static readonly StreamEnumUpdateStatus Stopped = new("stopped");

    public static StreamEnumUpdateStatus FromValue(string value) => FromValueCore(value);
}
