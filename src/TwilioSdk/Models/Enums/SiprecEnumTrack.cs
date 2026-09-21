using System.Text.Json.Serialization;
using TwilioSdk.Core.Enum;

namespace TwilioSdk.Models.Enums;

/// <summary>
/// One of <c>inbound_track</c>, <c>outbound_track</c>, <c>both_tracks</c>.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<SiprecEnumTrack>))]
public sealed record SiprecEnumTrack : StringEnum<SiprecEnumTrack>
{
    private SiprecEnumTrack(string value) : base(value)
    {
    }

    public static readonly SiprecEnumTrack InboundTrack = new("inbound_track");

    public static readonly SiprecEnumTrack OutboundTrack = new("outbound_track");

    public static readonly SiprecEnumTrack BothTracks = new("both_tracks");

    public static SiprecEnumTrack FromValue(string value) => FromValueCore(value);
}
