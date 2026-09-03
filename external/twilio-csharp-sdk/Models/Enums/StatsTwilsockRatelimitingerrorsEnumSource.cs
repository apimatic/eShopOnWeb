using System.Text.Json.Serialization;
using Twilio.Core.Enum;

namespace Twilio.Models.Enums;

/// <summary>
/// Source via which the request came from. Can be Twilsock.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<StatsTwilsockRatelimitingerrorsEnumSource>))]
public sealed record StatsTwilsockRatelimitingerrorsEnumSource : StringEnum<StatsTwilsockRatelimitingerrorsEnumSource>
{
    private StatsTwilsockRatelimitingerrorsEnumSource(string value) : base(value)
    {
    }

    public static readonly StatsTwilsockRatelimitingerrorsEnumSource Twilsock = new("TWILSOCK");

    public static StatsTwilsockRatelimitingerrorsEnumSource FromValue(string value) => FromValueCore(value);
}
