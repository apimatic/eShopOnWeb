using System.Text.Json.Serialization;
using Twilio.Core.Enum;

namespace Twilio.Models.Enums;

/// <summary>
/// The status of the Participant. Can be: <c>connected</c> or <c>disconnected</c>.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<RoomParticipantEnumStatus>))]
public sealed record RoomParticipantEnumStatus : StringEnum<RoomParticipantEnumStatus>
{
    private RoomParticipantEnumStatus(string value) : base(value)
    {
    }

    public static readonly RoomParticipantEnumStatus Connected = new("connected");

    public static readonly RoomParticipantEnumStatus Disconnected = new("disconnected");

    public static readonly RoomParticipantEnumStatus Reconnecting = new("reconnecting");

    public static RoomParticipantEnumStatus FromValue(string value) => FromValueCore(value);
}
