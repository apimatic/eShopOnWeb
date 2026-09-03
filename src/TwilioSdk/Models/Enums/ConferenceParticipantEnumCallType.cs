using System.Text.Json.Serialization;
using TwilioSdk.Core.Enum;

namespace TwilioSdk.Models.Enums;

[JsonConverter(typeof(StringEnumConverter<ConferenceParticipantEnumCallType>))]
public sealed record ConferenceParticipantEnumCallType : StringEnum<ConferenceParticipantEnumCallType>
{
    private ConferenceParticipantEnumCallType(string value) : base(value)
    {
    }

    public static readonly ConferenceParticipantEnumCallType Carrier = new("carrier");

    public static readonly ConferenceParticipantEnumCallType Client = new("client");

    public static readonly ConferenceParticipantEnumCallType Sip = new("sip");

    public static ConferenceParticipantEnumCallType FromValue(string value) => FromValueCore(value);
}
