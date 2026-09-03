using System.Text.Json.Serialization;
using TwilioSdk.Core.Enum;

namespace TwilioSdk.Models.Enums;

/// <summary>
/// The status of the recording. Can be: <c>processing</c>, <c>completed</c> and <c>absent</c>. For more detailed statuses on in-progress recordings, check out how to <see href="https://www.twilio.com/docs/voice/api/recording#update-a-recording-resource">Update a Recording Resource</see>.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<CallRecordingEnumStatus>))]
public sealed record CallRecordingEnumStatus : StringEnum<CallRecordingEnumStatus>
{
    private CallRecordingEnumStatus(string value) : base(value)
    {
    }

    public static readonly CallRecordingEnumStatus InProgress = new("in-progress");

    public static readonly CallRecordingEnumStatus Paused = new("paused");

    public static readonly CallRecordingEnumStatus Stopped = new("stopped");

    public static readonly CallRecordingEnumStatus Processing = new("processing");

    public static readonly CallRecordingEnumStatus Completed = new("completed");

    public static readonly CallRecordingEnumStatus Absent = new("absent");

    public static CallRecordingEnumStatus FromValue(string value) => FromValueCore(value);
}
