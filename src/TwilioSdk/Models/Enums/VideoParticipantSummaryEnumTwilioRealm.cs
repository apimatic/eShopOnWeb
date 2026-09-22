using System.Text.Json.Serialization;
using TwilioSdk.Core.Enum;

namespace TwilioSdk.Models.Enums;

[JsonConverter(typeof(StringEnumConverter<VideoParticipantSummaryEnumTwilioRealm>))]
public sealed record VideoParticipantSummaryEnumTwilioRealm : StringEnum<VideoParticipantSummaryEnumTwilioRealm>
{
    private VideoParticipantSummaryEnumTwilioRealm(string value) : base(value)
    {
    }

    public static readonly VideoParticipantSummaryEnumTwilioRealm Us1 = new("us1");

    public static readonly VideoParticipantSummaryEnumTwilioRealm Us2 = new("us2");

    public static readonly VideoParticipantSummaryEnumTwilioRealm Au1 = new("au1");

    public static readonly VideoParticipantSummaryEnumTwilioRealm Br1 = new("br1");

    public static readonly VideoParticipantSummaryEnumTwilioRealm Ie1 = new("ie1");

    public static readonly VideoParticipantSummaryEnumTwilioRealm Jp1 = new("jp1");

    public static readonly VideoParticipantSummaryEnumTwilioRealm Sg1 = new("sg1");

    public static readonly VideoParticipantSummaryEnumTwilioRealm In1 = new("in1");

    public static readonly VideoParticipantSummaryEnumTwilioRealm De1 = new("de1");

    public static readonly VideoParticipantSummaryEnumTwilioRealm Gll = new("gll");

    public static VideoParticipantSummaryEnumTwilioRealm FromValue(string value) => FromValueCore(value);
}
