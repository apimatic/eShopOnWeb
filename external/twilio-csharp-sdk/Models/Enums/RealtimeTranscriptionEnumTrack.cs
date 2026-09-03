using System.Text.Json.Serialization;
using Twilio.Core.Enum;

namespace Twilio.Models.Enums;

/// <summary>
/// One of <c>inbound_track</c>, <c>outbound_track</c>, <c>both_tracks</c>.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<RealtimeTranscriptionEnumTrack>))]
public sealed record RealtimeTranscriptionEnumTrack : StringEnum<RealtimeTranscriptionEnumTrack>
{
    private RealtimeTranscriptionEnumTrack(string value) : base(value)
    {
    }

    public static readonly RealtimeTranscriptionEnumTrack InboundTrack = new("inbound_track");

    public static readonly RealtimeTranscriptionEnumTrack OutboundTrack = new("outbound_track");

    public static readonly RealtimeTranscriptionEnumTrack BothTracks = new("both_tracks");

    public static RealtimeTranscriptionEnumTrack FromValue(string value) => FromValueCore(value);
}
