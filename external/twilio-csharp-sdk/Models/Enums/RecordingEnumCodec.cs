using System.Text.Json.Serialization;
using Twilio.Core.Enum;

namespace Twilio.Models.Enums;

/// <summary>
/// The codec used to encode the track. Can be: <c>VP8</c>, <c>H264</c>, <c>OPUS</c>, and <c>PCMU</c>.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<RecordingEnumCodec>))]
public sealed record RecordingEnumCodec : StringEnum<RecordingEnumCodec>
{
    private RecordingEnumCodec(string value) : base(value)
    {
    }

    public static readonly RecordingEnumCodec Vp8 = new("VP8");

    public static readonly RecordingEnumCodec H264 = new("H264");

    public static readonly RecordingEnumCodec Opus = new("OPUS");

    public static readonly RecordingEnumCodec Pcmu = new("PCMU");

    public static RecordingEnumCodec FromValue(string value) => FromValueCore(value);
}
