using System.Text.Json.Serialization;
using TwilioSdk.Core.Enum;

namespace TwilioSdk.Models.Enums;

/// <summary>
/// The reason why a conference ended. When a conference is in progress, will be <c>null</c>. When conference is completed, can be: <c>conference-ended-via-api</c>, <c>participant-with-end-conference-on-exit-left</c>, <c>participant-with-end-conference-on-exit-kicked</c>, <c>last-participant-kicked</c>, or <c>last-participant-left</c>.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<ConferenceEnumReasonConferenceEnded>))]
public sealed record ConferenceEnumReasonConferenceEnded : StringEnum<ConferenceEnumReasonConferenceEnded>
{
    private ConferenceEnumReasonConferenceEnded(string value) : base(value)
    {
    }

    public static readonly ConferenceEnumReasonConferenceEnded ConferenceEndedViaApi = new("conference-ended-via-api");

    public static readonly ConferenceEnumReasonConferenceEnded ParticipantWithEndConferenceOnExitLeft = new("participant-with-end-conference-on-exit-left");

    public static readonly ConferenceEnumReasonConferenceEnded ParticipantWithEndConferenceOnExitKicked = new("participant-with-end-conference-on-exit-kicked");

    public static readonly ConferenceEnumReasonConferenceEnded LastParticipantKicked = new("last-participant-kicked");

    public static readonly ConferenceEnumReasonConferenceEnded LastParticipantLeft = new("last-participant-left");

    public static ConferenceEnumReasonConferenceEnded FromValue(string value) => FromValueCore(value);
}
