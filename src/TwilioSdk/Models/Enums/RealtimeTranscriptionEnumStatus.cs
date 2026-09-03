using System.Text.Json.Serialization;
using TwilioSdk.Core.Enum;

namespace TwilioSdk.Models.Enums;

/// <summary>
/// The status - one of <c>stopped</c>, <c>in-flight</c>
/// </summary>
[JsonConverter(typeof(StringEnumConverter<RealtimeTranscriptionEnumStatus>))]
public sealed record RealtimeTranscriptionEnumStatus : StringEnum<RealtimeTranscriptionEnumStatus>
{
    private RealtimeTranscriptionEnumStatus(string value) : base(value)
    {
    }

    public static readonly RealtimeTranscriptionEnumStatus InProgress = new("in-progress");

    public static readonly RealtimeTranscriptionEnumStatus Stopped = new("stopped");

    public static RealtimeTranscriptionEnumStatus FromValue(string value) => FromValueCore(value);
}
