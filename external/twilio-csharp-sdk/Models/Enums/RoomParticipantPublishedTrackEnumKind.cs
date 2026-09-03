using System.Text.Json.Serialization;
using Twilio.Core.Enum;

namespace Twilio.Models.Enums;

/// <summary>
/// The track type. Can be: <c>audio</c>, <c>video</c> or <c>data</c>.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<RoomParticipantPublishedTrackEnumKind>))]
public sealed record RoomParticipantPublishedTrackEnumKind : StringEnum<RoomParticipantPublishedTrackEnumKind>
{
    private RoomParticipantPublishedTrackEnumKind(string value) : base(value)
    {
    }

    public static readonly RoomParticipantPublishedTrackEnumKind Audio = new("audio");

    public static readonly RoomParticipantPublishedTrackEnumKind Video = new("video");

    public static readonly RoomParticipantPublishedTrackEnumKind Data = new("data");

    public static RoomParticipantPublishedTrackEnumKind FromValue(string value) => FromValueCore(value);
}
