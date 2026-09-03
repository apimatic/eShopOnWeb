using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Twilio.Core.Models;
using Twilio.Core.Validation;
using Twilio.Core.Validation.Attributes;
using Twilio.Models.Enums;

namespace Twilio.Models;

public record InsightsV1ConferenceConferenceParticipant
{
    /// <summary>
    /// SID for this participant.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("participant_sid")]
    [StringLength(34, MinimumLength = 34)]
    [RegularExpression("^CP[0-9a-fA-F]{32}$")]
    public string? ParticipantSid { get; init; }

    /// <summary>
    /// The user-specified label of this participant.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("label")]
    public string? Label { get; init; }

    /// <summary>
    /// The unique SID identifier of the Conference.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("conference_sid")]
    [StringLength(34, MinimumLength = 34)]
    [RegularExpression("^CF[0-9a-fA-F]{32}$")]
    public string? ConferenceSid { get; init; }

    /// <summary>
    /// Unique SID identifier of the call that generated the Participant resource.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("call_sid")]
    [StringLength(34, MinimumLength = 34)]
    [RegularExpression("^CA[0-9a-fA-F]{32}$")]
    public string? CallSid { get; init; }

    /// <summary>
    /// The unique SID identifier of the Account.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("account_sid")]
    [StringLength(34, MinimumLength = 34)]
    [RegularExpression("^AC[0-9a-fA-F]{32}$")]
    public string? AccountSid { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("call_direction")]
    public ConferenceParticipantEnumCallDirection? CallDirection { get; init; }

    /// <summary>
    /// Caller ID of the calling party.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("from")]
    public string? From { get; init; }

    /// <summary>
    /// Called party.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("to")]
    public string? To { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("call_status")]
    public ConferenceParticipantEnumCallStatus? CallStatus { get; init; }

    /// <summary>
    /// ISO alpha-2 country code of the participant based on caller ID or called number.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("country_code")]
    public string? CountryCode { get; init; }

    /// <summary>
    /// Boolean. Indicates whether participant had startConferenceOnEnter=true or endConferenceOnExit=true.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("is_moderator")]
    public bool? IsModerator { get; init; }

    /// <summary>
    /// ISO 8601 timestamp of participant join event.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("join_time")]
    public DateTimeOffset? JoinTime { get; init; }

    /// <summary>
    /// ISO 8601 timestamp of participant leave event.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("leave_time")]
    public DateTimeOffset? LeaveTime { get; init; }

    /// <summary>
    /// Participant durations in seconds.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("duration_seconds")]
    public int? DurationSeconds { get; init; }

    /// <summary>
    /// Add Participant API only. Estimated time in queue at call creation.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("outbound_queue_length")]
    public int? OutboundQueueLength { get; init; }

    /// <summary>
    /// Add Participant API only. Actual time in queue in seconds.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("outbound_time_in_queue")]
    public int? OutboundTimeInQueue { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("jitter_buffer_size")]
    public ConferenceParticipantEnumJitterBufferSize? JitterBufferSize { get; init; }

    /// <summary>
    /// Boolean. Indicated whether participant was a coach.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("is_coach")]
    public bool? IsCoach { get; init; }

    /// <summary>
    /// Call SIDs coached by this participant.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("coached_participants")]
    public IReadOnlyList<string?>? CoachedParticipants { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("participant_region")]
    public ConferenceParticipantEnumRegion? ParticipantRegion { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("conference_region")]
    public ConferenceParticipantEnumRegion? ConferenceRegion { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("call_type")]
    public ConferenceParticipantEnumCallType? CallType { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("processing_state")]
    public ConferenceParticipantEnumProcessingState? ProcessingState { get; init; }

    /// <summary>
    /// Participant properties and metadata.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("properties")]
    public object? Properties { get; init; }

    /// <summary>
    /// Object containing information of actions taken by participants. Contains a dictionary of URL links to nested resources of this Conference Participant.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("events")]
    public object? Events { get; init; }

    /// <summary>
    /// Object. Contains participant call quality metrics.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("metrics")]
    public object? Metrics { get; init; }

    /// <summary>
    /// The URL of this resource.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("url")]
    [Format(FormatKind.Uri)]
    public string? Url { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
