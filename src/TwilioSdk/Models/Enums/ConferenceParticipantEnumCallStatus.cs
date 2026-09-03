using System.Text.Json.Serialization;
using TwilioSdk.Core.Enum;

namespace TwilioSdk.Models.Enums;

[JsonConverter(typeof(StringEnumConverter<ConferenceParticipantEnumCallStatus>))]
public sealed record ConferenceParticipantEnumCallStatus : StringEnum<ConferenceParticipantEnumCallStatus>
{
    private ConferenceParticipantEnumCallStatus(string value) : base(value)
    {
    }

    public static readonly ConferenceParticipantEnumCallStatus Answered = new("answered");

    public static readonly ConferenceParticipantEnumCallStatus Completed = new("completed");

    public static readonly ConferenceParticipantEnumCallStatus Busy = new("busy");

    public static readonly ConferenceParticipantEnumCallStatus Fail = new("fail");

    public static readonly ConferenceParticipantEnumCallStatus Noanswer = new("noanswer");

    public static readonly ConferenceParticipantEnumCallStatus Ringing = new("ringing");

    public static readonly ConferenceParticipantEnumCallStatus Canceled = new("canceled");

    public static ConferenceParticipantEnumCallStatus FromValue(string value) => FromValueCore(value);
}
