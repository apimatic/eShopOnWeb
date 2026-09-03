using System.Text.Json.Serialization;
using Twilio.Core.Enum;

namespace Twilio.Models.Enums;

[JsonConverter(typeof(StringEnumConverter<ConferenceParticipantEnumRegion>))]
public sealed record ConferenceParticipantEnumRegion : StringEnum<ConferenceParticipantEnumRegion>
{
    private ConferenceParticipantEnumRegion(string value) : base(value)
    {
    }

    public static readonly ConferenceParticipantEnumRegion Us1 = new("us1");

    public static readonly ConferenceParticipantEnumRegion Us2 = new("us2");

    public static readonly ConferenceParticipantEnumRegion Au1 = new("au1");

    public static readonly ConferenceParticipantEnumRegion Br1 = new("br1");

    public static readonly ConferenceParticipantEnumRegion Ie1 = new("ie1");

    public static readonly ConferenceParticipantEnumRegion Jp1 = new("jp1");

    public static readonly ConferenceParticipantEnumRegion Sg1 = new("sg1");

    public static readonly ConferenceParticipantEnumRegion De1 = new("de1");

    public static readonly ConferenceParticipantEnumRegion In1 = new("in1");

    public static ConferenceParticipantEnumRegion FromValue(string value) => FromValueCore(value);
}
