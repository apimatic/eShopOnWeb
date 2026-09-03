using System.Text.Json.Serialization;
using Twilio.Core.Enum;

namespace Twilio.Models.Enums;

[JsonConverter(typeof(StringEnumConverter<VideoRoomSummaryEnumTwilioRealm>))]
public sealed record VideoRoomSummaryEnumTwilioRealm : StringEnum<VideoRoomSummaryEnumTwilioRealm>
{
    private VideoRoomSummaryEnumTwilioRealm(string value) : base(value)
    {
    }

    public static readonly VideoRoomSummaryEnumTwilioRealm Us1 = new("us1");

    public static readonly VideoRoomSummaryEnumTwilioRealm Us2 = new("us2");

    public static readonly VideoRoomSummaryEnumTwilioRealm Au1 = new("au1");

    public static readonly VideoRoomSummaryEnumTwilioRealm Br1 = new("br1");

    public static readonly VideoRoomSummaryEnumTwilioRealm Ie1 = new("ie1");

    public static readonly VideoRoomSummaryEnumTwilioRealm Jp1 = new("jp1");

    public static readonly VideoRoomSummaryEnumTwilioRealm Sg1 = new("sg1");

    public static readonly VideoRoomSummaryEnumTwilioRealm In1 = new("in1");

    public static readonly VideoRoomSummaryEnumTwilioRealm De1 = new("de1");

    public static readonly VideoRoomSummaryEnumTwilioRealm Gll = new("gll");

    public static VideoRoomSummaryEnumTwilioRealm FromValue(string value) => FromValueCore(value);
}
