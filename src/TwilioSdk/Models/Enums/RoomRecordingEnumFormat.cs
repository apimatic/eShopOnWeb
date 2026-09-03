using System.Text.Json.Serialization;
using TwilioSdk.Core.Enum;

namespace TwilioSdk.Models.Enums;

[JsonConverter(typeof(StringEnumConverter<RoomRecordingEnumFormat>))]
public sealed record RoomRecordingEnumFormat : StringEnum<RoomRecordingEnumFormat>
{
    private RoomRecordingEnumFormat(string value) : base(value)
    {
    }

    public static readonly RoomRecordingEnumFormat Mka = new("mka");

    public static readonly RoomRecordingEnumFormat Mkv = new("mkv");

    public static RoomRecordingEnumFormat FromValue(string value) => FromValueCore(value);
}
