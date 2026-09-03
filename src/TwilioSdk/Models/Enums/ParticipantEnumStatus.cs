using System.Text.Json.Serialization;
using TwilioSdk.Core.Enum;

namespace TwilioSdk.Models.Enums;

/// <summary>
/// The status of the participant's call in a session. Can be: <c>queued</c>, <c>connecting</c>, <c>ringing</c>, <c>connected</c>, <c>complete</c>, or <c>failed</c>.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<ParticipantEnumStatus>))]
public sealed record ParticipantEnumStatus : StringEnum<ParticipantEnumStatus>
{
    private ParticipantEnumStatus(string value) : base(value)
    {
    }

    public static readonly ParticipantEnumStatus Queued = new("queued");

    public static readonly ParticipantEnumStatus Connecting = new("connecting");

    public static readonly ParticipantEnumStatus Ringing = new("ringing");

    public static readonly ParticipantEnumStatus Connected = new("connected");

    public static readonly ParticipantEnumStatus Complete = new("complete");

    public static readonly ParticipantEnumStatus Failed = new("failed");

    public static ParticipantEnumStatus FromValue(string value) => FromValueCore(value);
}
