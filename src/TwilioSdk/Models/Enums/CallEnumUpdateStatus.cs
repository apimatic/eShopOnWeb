using System.Text.Json.Serialization;
using TwilioSdk.Core.Enum;

namespace TwilioSdk.Models.Enums;

[JsonConverter(typeof(StringEnumConverter<CallEnumUpdateStatus>))]
public sealed record CallEnumUpdateStatus : StringEnum<CallEnumUpdateStatus>
{
    private CallEnumUpdateStatus(string value) : base(value)
    {
    }

    public static readonly CallEnumUpdateStatus Canceled = new("canceled");

    public static readonly CallEnumUpdateStatus Completed = new("completed");

    public static CallEnumUpdateStatus FromValue(string value) => FromValueCore(value);
}
