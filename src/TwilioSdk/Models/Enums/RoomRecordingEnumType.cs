using System.Text.Json.Serialization;
using TwilioSdk.Core.Enum;

namespace TwilioSdk.Models.Enums;

/// <summary>
/// The recording's media type. Can be: <c>audio</c> or <c>video</c>.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<RoomRecordingEnumType>))]
public sealed record RoomRecordingEnumType : StringEnum<RoomRecordingEnumType>
{
    private RoomRecordingEnumType(string value) : base(value)
    {
    }

    public static readonly RoomRecordingEnumType Audio = new("audio");

    public static readonly RoomRecordingEnumType Video = new("video");

    public static readonly RoomRecordingEnumType Data = new("data");

    public static RoomRecordingEnumType FromValue(string value) => FromValueCore(value);
}
