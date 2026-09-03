using System.Text.Json.Serialization;
using TwilioSdk.Core.Enum;

namespace TwilioSdk.Models.Enums;

/// <summary>
/// The tracks to be included in the Stream. Possible values are <c>inbound_track</c>, <c>outbound_track</c>, <c>both_tracks</c>. Default value is <c>inbound_track</c>.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<StreamEnumTrack>))]
public sealed record StreamEnumTrack : StringEnum<StreamEnumTrack>
{
    private StreamEnumTrack(string value) : base(value)
    {
    }

    public static readonly StreamEnumTrack InboundTrack = new("inbound_track");

    public static readonly StreamEnumTrack OutboundTrack = new("outbound_track");

    public static readonly StreamEnumTrack BothTracks = new("both_tracks");

    public static StreamEnumTrack FromValue(string value) => FromValueCore(value);
}
