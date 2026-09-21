using System.Text.Json.Serialization;
using TwilioSdk.Core.Enum;

namespace TwilioSdk.Models.Enums;

[JsonConverter(typeof(StringEnumConverter<ConferenceParticipantEnumProcessingState>))]
public sealed record ConferenceParticipantEnumProcessingState : StringEnum<ConferenceParticipantEnumProcessingState>
{
    private ConferenceParticipantEnumProcessingState(string value) : base(value)
    {
    }

    public static readonly ConferenceParticipantEnumProcessingState Complete = new("complete");

    public static readonly ConferenceParticipantEnumProcessingState InProgress = new("in_progress");

    public static readonly ConferenceParticipantEnumProcessingState Timeout = new("timeout");

    public static ConferenceParticipantEnumProcessingState FromValue(string value) => FromValueCore(value);
}
