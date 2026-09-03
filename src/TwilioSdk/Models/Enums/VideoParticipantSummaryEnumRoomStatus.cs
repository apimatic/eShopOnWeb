using System.Text.Json.Serialization;
using TwilioSdk.Core.Enum;

namespace TwilioSdk.Models.Enums;

[JsonConverter(typeof(StringEnumConverter<VideoParticipantSummaryEnumRoomStatus>))]
public sealed record VideoParticipantSummaryEnumRoomStatus : StringEnum<VideoParticipantSummaryEnumRoomStatus>
{
    private VideoParticipantSummaryEnumRoomStatus(string value) : base(value)
    {
    }

    public static readonly VideoParticipantSummaryEnumRoomStatus InProgress = new("in_progress");

    public static readonly VideoParticipantSummaryEnumRoomStatus Connected = new("connected");

    public static readonly VideoParticipantSummaryEnumRoomStatus Completed = new("completed");

    public static readonly VideoParticipantSummaryEnumRoomStatus Disconnected = new("disconnected");

    public static VideoParticipantSummaryEnumRoomStatus FromValue(string value) => FromValueCore(value);
}
