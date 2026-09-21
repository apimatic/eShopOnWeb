using System.Text.Json.Serialization;
using TwilioSdk.Core.Enum;

namespace TwilioSdk.Models.Enums;

[JsonConverter(typeof(StringEnumConverter<VideoRoomSummaryEnumRoomType>))]
public sealed record VideoRoomSummaryEnumRoomType : StringEnum<VideoRoomSummaryEnumRoomType>
{
    private VideoRoomSummaryEnumRoomType(string value) : base(value)
    {
    }

    public static readonly VideoRoomSummaryEnumRoomType Go = new("go");

    public static readonly VideoRoomSummaryEnumRoomType PeerToPeer = new("peer_to_peer");

    public static readonly VideoRoomSummaryEnumRoomType Group = new("group");

    public static readonly VideoRoomSummaryEnumRoomType GroupSmall = new("group_small");

    public static VideoRoomSummaryEnumRoomType FromValue(string value) => FromValueCore(value);
}
