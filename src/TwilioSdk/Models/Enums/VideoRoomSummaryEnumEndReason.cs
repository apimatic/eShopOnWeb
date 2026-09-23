using System.Text.Json.Serialization;
using TwilioSdk.Core.Enum;

namespace TwilioSdk.Models.Enums;

[JsonConverter(typeof(StringEnumConverter<VideoRoomSummaryEnumEndReason>))]
public sealed record VideoRoomSummaryEnumEndReason : StringEnum<VideoRoomSummaryEnumEndReason>
{
    private VideoRoomSummaryEnumEndReason(string value) : base(value)
    {
    }

    public static readonly VideoRoomSummaryEnumEndReason RoomEndedViaApi = new("room_ended_via_api");

    public static readonly VideoRoomSummaryEnumEndReason Timeout = new("timeout");

    public static VideoRoomSummaryEnumEndReason FromValue(string value) => FromValueCore(value);
}
