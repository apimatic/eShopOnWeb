using System.Text.Json.Serialization;
using TwilioSdk.Core.Enum;

namespace TwilioSdk.Models.Enums;

[JsonConverter(typeof(StringEnumConverter<VideoParticipantSummaryEnumCodec>))]
public sealed record VideoParticipantSummaryEnumCodec : StringEnum<VideoParticipantSummaryEnumCodec>
{
    private VideoParticipantSummaryEnumCodec(string value) : base(value)
    {
    }

    public static readonly VideoParticipantSummaryEnumCodec Vp8 = new("VP8");

    public static readonly VideoParticipantSummaryEnumCodec H264 = new("H264");

    public static readonly VideoParticipantSummaryEnumCodec Vp9 = new("VP9");

    public static readonly VideoParticipantSummaryEnumCodec Opus = new("opus");

    public static VideoParticipantSummaryEnumCodec FromValue(string value) => FromValueCore(value);
}
