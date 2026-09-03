using System.Text.Json.Serialization;
using Twilio.Core.Enum;

namespace Twilio.Models.Enums;

/// <summary>
/// The track type. Can be: <c>audio</c>, <c>video</c> or <c>data</c>.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<RoomParticipantSubscribedTrackEnumKind>))]
public sealed record RoomParticipantSubscribedTrackEnumKind : StringEnum<RoomParticipantSubscribedTrackEnumKind>
{
    private RoomParticipantSubscribedTrackEnumKind(string value) : base(value)
    {
    }

    public static readonly RoomParticipantSubscribedTrackEnumKind Audio = new("audio");

    public static readonly RoomParticipantSubscribedTrackEnumKind Video = new("video");

    public static readonly RoomParticipantSubscribedTrackEnumKind Data = new("data");

    public static RoomParticipantSubscribedTrackEnumKind FromValue(string value) => FromValueCore(value);
}
