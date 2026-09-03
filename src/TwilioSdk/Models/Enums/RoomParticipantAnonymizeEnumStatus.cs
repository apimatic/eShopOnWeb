using System.Text.Json.Serialization;
using TwilioSdk.Core.Enum;

namespace TwilioSdk.Models.Enums;

/// <summary>
/// The status of the Participant. Can be: <c>connected</c> or <c>disconnected</c>.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<RoomParticipantAnonymizeEnumStatus>))]
public sealed record RoomParticipantAnonymizeEnumStatus : StringEnum<RoomParticipantAnonymizeEnumStatus>
{
    private RoomParticipantAnonymizeEnumStatus(string value) : base(value)
    {
    }

    public static readonly RoomParticipantAnonymizeEnumStatus Connected = new("connected");

    public static readonly RoomParticipantAnonymizeEnumStatus Disconnected = new("disconnected");

    public static RoomParticipantAnonymizeEnumStatus FromValue(string value) => FromValueCore(value);
}
