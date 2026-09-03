using System.Text.Json.Serialization;
using Twilio.Core.Enum;

namespace Twilio.Models.Enums;

/// <summary>
/// The codec used for the recording. Can be: <c>VP8</c> or <c>H264</c>.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<RoomRecordingEnumCodec>))]
public sealed record RoomRecordingEnumCodec : StringEnum<RoomRecordingEnumCodec>
{
    private RoomRecordingEnumCodec(string value) : base(value)
    {
    }

    public static readonly RoomRecordingEnumCodec Vp8 = new("VP8");

    public static readonly RoomRecordingEnumCodec H264 = new("H264");

    public static readonly RoomRecordingEnumCodec Opus = new("OPUS");

    public static readonly RoomRecordingEnumCodec Pcmu = new("PCMU");

    public static RoomRecordingEnumCodec FromValue(string value) => FromValueCore(value);
}
