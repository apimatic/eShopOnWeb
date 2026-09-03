using System.Text.Json.Serialization;
using TwilioSdk.Core.Enum;

namespace TwilioSdk.Models.Enums;

[JsonConverter(typeof(StringEnumConverter<RoomEnumVideoCodec>))]
public sealed record RoomEnumVideoCodec : StringEnum<RoomEnumVideoCodec>
{
    private RoomEnumVideoCodec(string value) : base(value)
    {
    }

    public static readonly RoomEnumVideoCodec Vp8 = new("VP8");

    public static readonly RoomEnumVideoCodec H264 = new("H264");

    public static RoomEnumVideoCodec FromValue(string value) => FromValueCore(value);
}
