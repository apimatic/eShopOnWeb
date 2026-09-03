using System.Text.Json.Serialization;
using Twilio.Core.Enum;

namespace Twilio.Models.Enums;

/// <summary>
/// The status of the recording. Can be: <c>processing</c>, <c>completed</c>, or <c>deleted</c>. <c>processing</c> indicates the recording is still being captured; <c>completed</c> indicates the recording has been captured and is now available for download. <c>deleted</c> means the recording media has been deleted from the system, but its metadata is still available.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<RecordingEnumStatus1>))]
public sealed record RecordingEnumStatus1 : StringEnum<RecordingEnumStatus1>
{
    private RecordingEnumStatus1(string value) : base(value)
    {
    }

    public static readonly RecordingEnumStatus1 Processing = new("processing");

    public static readonly RecordingEnumStatus1 Completed = new("completed");

    public static readonly RecordingEnumStatus1 Deleted = new("deleted");

    public static readonly RecordingEnumStatus1 Failed = new("failed");

    public static RecordingEnumStatus1 FromValue(string value) => FromValueCore(value);
}
