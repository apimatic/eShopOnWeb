using System.Collections.Generic;
using System.Text.Json.Serialization;
using Twilio.Core.Models;

namespace Twilio.Models;

/// <summary>
/// KYT metrics for outbound carrier calling.
/// </summary>
public record OutboundCarrierCalling
{
    /// <summary>
    /// Number of unique PSTN calling numbers to non-Twilio numbers during the report period.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("unique_calling_numbers")]
    public int? UniqueCallingNumbers { get; init; }

    /// <summary>
    /// Number of unique non-Twilio PSTN called numbers during the report period.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("unique_called_numbers")]
    public int? UniqueCalledNumbers { get; init; }

    /// <summary>
    /// Percentage of blocked calls by carrier per country.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("blocked_calls_by_carrier")]
    public IReadOnlyList<CountyCarrierValue>? BlockedCallsByCarrier { get; init; }

    /// <summary>
    /// Percentage of completed outbound calls under 10 seconds (PSTN Short call tags); More than 15% is typically low trust measured.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("short_duration_calls_percentage")]
    public double? ShortDurationCallsPercentage { get; init; }

    /// <summary>
    /// Percentage of long duration calls ( &gt;= 60 seconds)
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("long_duration_calls_percentage")]
    public double? LongDurationCallsPercentage { get; init; }

    /// <summary>
    /// Percentage of completed outbound calls to unassigned or unallocated phone numbers.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("potential_robocalls_percentage")]
    public double? PotentialRobocallsPercentage { get; init; }

    /// <summary>
    /// Metrics related to Branded Calling bundled calls including CTIA for the report period.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("branded_calling")]
    public BrandedCalling? BrandedCalling { get; init; }

    /// <summary>
    /// Metrics related to Voice Integrity enabled calls for the report period.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("voice_integrity")]
    public VoiceIntegrity? VoiceIntegrity { get; init; }

    /// <summary>
    /// Metrics related to STIR/SHAKEN attestation A, B, and C for the report period.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("stir_shaken")]
    public StirShaken? StirShaken { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
