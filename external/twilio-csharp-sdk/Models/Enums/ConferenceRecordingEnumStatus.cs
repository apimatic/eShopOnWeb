using System.Text.Json.Serialization;
using Twilio.Core.Enum;

namespace Twilio.Models.Enums;

/// <summary>
/// The status of the recording. Can be: <c>processing</c>, <c>completed</c> and <c>absent</c>. For more detailed statuses on in-progress recordings, check out how to <see href="https://www.twilio.com/docs/voice/api/recording#update-a-recording-resource">Update a Recording Resource</see>.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<ConferenceRecordingEnumStatus>))]
public sealed record ConferenceRecordingEnumStatus : StringEnum<ConferenceRecordingEnumStatus>
{
    private ConferenceRecordingEnumStatus(string value) : base(value)
    {
    }

    public static readonly ConferenceRecordingEnumStatus InProgress = new("in-progress");

    public static readonly ConferenceRecordingEnumStatus Paused = new("paused");

    public static readonly ConferenceRecordingEnumStatus Stopped = new("stopped");

    public static readonly ConferenceRecordingEnumStatus Processing = new("processing");

    public static readonly ConferenceRecordingEnumStatus Completed = new("completed");

    public static readonly ConferenceRecordingEnumStatus Absent = new("absent");

    public static ConferenceRecordingEnumStatus FromValue(string value) => FromValueCore(value);
}
