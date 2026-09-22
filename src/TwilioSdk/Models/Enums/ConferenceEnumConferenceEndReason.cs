using System.Text.Json.Serialization;
using TwilioSdk.Core.Enum;

namespace TwilioSdk.Models.Enums;

[JsonConverter(typeof(StringEnumConverter<ConferenceEnumConferenceEndReason>))]
public sealed record ConferenceEnumConferenceEndReason : StringEnum<ConferenceEnumConferenceEndReason>
{
    private ConferenceEnumConferenceEndReason(string value) : base(value)
    {
    }

    public static readonly ConferenceEnumConferenceEndReason LastParticipantLeft = new("last_participant_left");

    public static readonly ConferenceEnumConferenceEndReason ConferenceEndedViaApi = new("conference_ended_via_api");

    public static readonly ConferenceEnumConferenceEndReason ParticipantWithEndConferenceOnExitLeft = new("participant_with_end_conference_on_exit_left");

    public static readonly ConferenceEnumConferenceEndReason LastParticipantKicked = new("last_participant_kicked");

    public static readonly ConferenceEnumConferenceEndReason ParticipantWithEndConferenceOnExitKicked = new("participant_with_end_conference_on_exit_kicked");

    public static ConferenceEnumConferenceEndReason FromValue(string value) => FromValueCore(value);
}
