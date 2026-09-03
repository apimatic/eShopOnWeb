using System.Text.Json.Serialization;
using TwilioSdk.Core.Enum;

namespace TwilioSdk.Models.Enums;

/// <summary>
/// The status of the recording. Can be: <c>processing</c>, <c>completed</c>, or <c>deleted</c>. <c>processing</c> indicates the Recording is still being captured. <c>completed</c> indicates the Recording has been captured and is now available for download. <c>deleted</c> means the recording media has been deleted from the system, but its metadata is still available for historical purposes.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<RoomRecordingEnumStatus>))]
public sealed record RoomRecordingEnumStatus : StringEnum<RoomRecordingEnumStatus>
{
    private RoomRecordingEnumStatus(string value) : base(value)
    {
    }

    public static readonly RoomRecordingEnumStatus Processing = new("processing");

    public static readonly RoomRecordingEnumStatus Completed = new("completed");

    public static readonly RoomRecordingEnumStatus Deleted = new("deleted");

    public static readonly RoomRecordingEnumStatus Failed = new("failed");

    public static RoomRecordingEnumStatus FromValue(string value) => FromValueCore(value);
}
