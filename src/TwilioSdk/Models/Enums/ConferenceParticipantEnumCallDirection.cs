using System.Text.Json.Serialization;
using TwilioSdk.Core.Enum;

namespace TwilioSdk.Models.Enums;

[JsonConverter(typeof(StringEnumConverter<ConferenceParticipantEnumCallDirection>))]
public sealed record ConferenceParticipantEnumCallDirection : StringEnum<ConferenceParticipantEnumCallDirection>
{
    private ConferenceParticipantEnumCallDirection(string value) : base(value)
    {
    }

    public static readonly ConferenceParticipantEnumCallDirection Inbound = new("inbound");

    public static readonly ConferenceParticipantEnumCallDirection Outbound = new("outbound");

    public static ConferenceParticipantEnumCallDirection FromValue(string value) => FromValueCore(value);
}
