using System.Text.Json.Serialization;
using TwilioSdk.Core.Enum;

namespace TwilioSdk.Models.Enums;

/// <summary>
/// The status of the transcriptions resource.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<RoomTranscriptionsEnumStatus>))]
public sealed record RoomTranscriptionsEnumStatus : StringEnum<RoomTranscriptionsEnumStatus>
{
    private RoomTranscriptionsEnumStatus(string value) : base(value)
    {
    }

    public static readonly RoomTranscriptionsEnumStatus Started = new("started");

    public static readonly RoomTranscriptionsEnumStatus Stopped = new("stopped");

    public static readonly RoomTranscriptionsEnumStatus Failed = new("failed");

    public static RoomTranscriptionsEnumStatus FromValue(string value) => FromValueCore(value);
}
