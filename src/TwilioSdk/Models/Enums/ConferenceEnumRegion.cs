using System.Text.Json.Serialization;
using TwilioSdk.Core.Enum;

namespace TwilioSdk.Models.Enums;

[JsonConverter(typeof(StringEnumConverter<ConferenceEnumRegion>))]
public sealed record ConferenceEnumRegion : StringEnum<ConferenceEnumRegion>
{
    private ConferenceEnumRegion(string value) : base(value)
    {
    }

    public static readonly ConferenceEnumRegion Us1 = new("us1");

    public static readonly ConferenceEnumRegion Us2 = new("us2");

    public static readonly ConferenceEnumRegion Au1 = new("au1");

    public static readonly ConferenceEnumRegion Br1 = new("br1");

    public static readonly ConferenceEnumRegion Ie1 = new("ie1");

    public static readonly ConferenceEnumRegion Jp1 = new("jp1");

    public static readonly ConferenceEnumRegion Sg1 = new("sg1");

    public static readonly ConferenceEnumRegion De1 = new("de1");

    public static readonly ConferenceEnumRegion In1 = new("in1");

    public static ConferenceEnumRegion FromValue(string value) => FromValueCore(value);
}
