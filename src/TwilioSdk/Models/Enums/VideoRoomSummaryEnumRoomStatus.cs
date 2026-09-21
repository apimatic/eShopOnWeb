using System.Text.Json.Serialization;
using TwilioSdk.Core.Enum;

namespace TwilioSdk.Models.Enums;

[JsonConverter(typeof(StringEnumConverter<VideoRoomSummaryEnumRoomStatus>))]
public sealed record VideoRoomSummaryEnumRoomStatus : StringEnum<VideoRoomSummaryEnumRoomStatus>
{
    private VideoRoomSummaryEnumRoomStatus(string value) : base(value)
    {
    }

    public static readonly VideoRoomSummaryEnumRoomStatus InProgress = new("in_progress");

    public static readonly VideoRoomSummaryEnumRoomStatus Completed = new("completed");

    public static VideoRoomSummaryEnumRoomStatus FromValue(string value) => FromValueCore(value);
}
