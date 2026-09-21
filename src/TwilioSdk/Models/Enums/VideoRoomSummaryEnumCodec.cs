using System.Text.Json.Serialization;
using TwilioSdk.Core.Enum;

namespace TwilioSdk.Models.Enums;

[JsonConverter(typeof(StringEnumConverter<VideoRoomSummaryEnumCodec>))]
public sealed record VideoRoomSummaryEnumCodec : StringEnum<VideoRoomSummaryEnumCodec>
{
    private VideoRoomSummaryEnumCodec(string value) : base(value)
    {
    }

    public static readonly VideoRoomSummaryEnumCodec Vp8 = new("VP8");

    public static readonly VideoRoomSummaryEnumCodec H264 = new("H264");

    public static readonly VideoRoomSummaryEnumCodec Vp9 = new("VP9");

    public static readonly VideoRoomSummaryEnumCodec Opus = new("opus");

    public static VideoRoomSummaryEnumCodec FromValue(string value) => FromValueCore(value);
}
