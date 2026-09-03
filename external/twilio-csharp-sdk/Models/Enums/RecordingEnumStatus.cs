using System.Text.Json.Serialization;
using Twilio.Core.Enum;

namespace Twilio.Models.Enums;

/// <summary>
/// The status of the recording. Can be: <c>processing</c>, <c>completed</c>, <c>absent</c> or <c>deleted</c>. For information about more detailed statuses on in-progress recordings, check out how to <see href="https://www.twilio.com/docs/voice/api/recording#update-a-recording-resource">Update a Recording Resource</see>.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<RecordingEnumStatus>))]
public sealed record RecordingEnumStatus : StringEnum<RecordingEnumStatus>
{
    private RecordingEnumStatus(string value) : base(value)
    {
    }

    public static readonly RecordingEnumStatus InProgress = new("in-progress");

    public static readonly RecordingEnumStatus Paused = new("paused");

    public static readonly RecordingEnumStatus Stopped = new("stopped");

    public static readonly RecordingEnumStatus Processing = new("processing");

    public static readonly RecordingEnumStatus Completed = new("completed");

    public static readonly RecordingEnumStatus Absent = new("absent");

    public static readonly RecordingEnumStatus Deleted = new("deleted");

    public static RecordingEnumStatus FromValue(string value) => FromValueCore(value);
}
