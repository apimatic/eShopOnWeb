using System.Text.Json.Serialization;
using Twilio.Core.Enum;

namespace Twilio.Models.Enums;

[JsonConverter(typeof(StringEnumConverter<VideoRoomSummaryEnumProcessingState>))]
public sealed record VideoRoomSummaryEnumProcessingState : StringEnum<VideoRoomSummaryEnumProcessingState>
{
    private VideoRoomSummaryEnumProcessingState(string value) : base(value)
    {
    }

    public static readonly VideoRoomSummaryEnumProcessingState Complete = new("complete");

    public static readonly VideoRoomSummaryEnumProcessingState InProgress = new("in_progress");

    public static readonly VideoRoomSummaryEnumProcessingState Timeout = new("timeout");

    public static readonly VideoRoomSummaryEnumProcessingState NotStarted = new("not_started");

    public static VideoRoomSummaryEnumProcessingState FromValue(string value) => FromValueCore(value);
}
