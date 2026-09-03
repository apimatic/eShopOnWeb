using System.Text.Json.Serialization;
using TwilioSdk.Core.Enum;

namespace TwilioSdk.Models.Enums;

[JsonConverter(typeof(StringEnumConverter<RealtimeTranscriptionEnumUpdateStatus>))]
public sealed record RealtimeTranscriptionEnumUpdateStatus : StringEnum<RealtimeTranscriptionEnumUpdateStatus>
{
    private RealtimeTranscriptionEnumUpdateStatus(string value) : base(value)
    {
    }

    public static readonly RealtimeTranscriptionEnumUpdateStatus Stopped = new("stopped");

    public static RealtimeTranscriptionEnumUpdateStatus FromValue(string value) => FromValueCore(value);
}
