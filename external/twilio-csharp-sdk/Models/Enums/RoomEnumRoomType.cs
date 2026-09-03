using System.Text.Json.Serialization;
using Twilio.Core.Enum;

namespace Twilio.Models.Enums;

/// <summary>
/// Type of room. Use <c>group</c> for new implementations. <c>go</c>, <c>peer-to-peer</c>, and <c>group-small</c> are deprecated.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<RoomEnumRoomType>))]
public sealed record RoomEnumRoomType : StringEnum<RoomEnumRoomType>
{
    private RoomEnumRoomType(string value) : base(value)
    {
    }

    public static readonly RoomEnumRoomType Group = new("group");

    public static readonly RoomEnumRoomType Go = new("go");

    public static readonly RoomEnumRoomType PeerToPeer = new("peer-to-peer");

    public static readonly RoomEnumRoomType GroupSmall = new("group-small");

    public static RoomEnumRoomType FromValue(string value) => FromValueCore(value);
}
