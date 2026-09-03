using System.Text.Json.Serialization;
using Twilio.Core.Enum;

namespace Twilio.Models.Enums;

[JsonConverter(typeof(StringEnumConverter<ConferenceEnumTag>))]
public sealed record ConferenceEnumTag : StringEnum<ConferenceEnumTag>
{
    private ConferenceEnumTag(string value) : base(value)
    {
    }

    public static readonly ConferenceEnumTag InvalidRequestedRegion = new("invalid_requested_region");

    public static readonly ConferenceEnumTag DuplicateIdentity = new("duplicate_identity");

    public static readonly ConferenceEnumTag StartFailure = new("start_failure");

    public static readonly ConferenceEnumTag RegionConfigurationIssues = new("region_configuration_issues");

    public static readonly ConferenceEnumTag QualityWarnings = new("quality_warnings");

    public static readonly ConferenceEnumTag ParticipantBehaviorIssues = new("participant_behavior_issues");

    public static readonly ConferenceEnumTag HighPacketLoss = new("high_packet_loss");

    public static readonly ConferenceEnumTag HighJitter = new("high_jitter");

    public static readonly ConferenceEnumTag HighLatency = new("high_latency");

    public static readonly ConferenceEnumTag LowMos = new("low_mos");

    public static readonly ConferenceEnumTag DetectedSilence = new("detected_silence");

    public static readonly ConferenceEnumTag NoConcurrentParticipants = new("no_concurrent_participants");

    public static ConferenceEnumTag FromValue(string value) => FromValueCore(value);
}
