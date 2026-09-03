using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using TwilioSdk.Core.Models;
using TwilioSdk.Core.Validation;
using TwilioSdk.Core.Validation.Attributes;
using TwilioSdk.Models.Enums;

namespace TwilioSdk.Models;

public record InsightsV1Conference
{
    /// <summary>
    /// The unique SID identifier of the Conference.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("conference_sid")]
    [StringLength(34, MinimumLength = 34)]
    [RegularExpression("^CF[0-9a-fA-F]{32}$")]
    public string? ConferenceSid { get; init; }

    /// <summary>
    /// The unique SID identifier of the Account.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("account_sid")]
    [StringLength(34, MinimumLength = 34)]
    [RegularExpression("^AC[0-9a-fA-F]{32}$")]
    public string? AccountSid { get; init; }

    /// <summary>
    /// Custom label for the conference resource, up to 64 characters.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("friendly_name")]
    public string? FriendlyName { get; init; }

    /// <summary>
    /// Conference creation date and time in ISO 8601 format.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("create_time")]
    public DateTimeOffset? CreateTime { get; init; }

    /// <summary>
    /// Timestamp in ISO 8601 format when the conference started. Conferences do not start until at least two participants join, at least one of whom has startConferenceOnEnter=true.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("start_time")]
    public DateTimeOffset? StartTime { get; init; }

    /// <summary>
    /// Conference end date and time in ISO 8601 format.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("end_time")]
    public DateTimeOffset? EndTime { get; init; }

    /// <summary>
    /// Conference duration in seconds.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("duration_seconds")]
    public int? DurationSeconds { get; init; }

    /// <summary>
    /// Duration of the between conference start event and conference end event in seconds.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("connect_duration_seconds")]
    public int? ConnectDurationSeconds { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("status")]
    public ConferenceEnumConferenceStatus? Status { get; init; }

    /// <summary>
    /// Maximum number of concurrent participants as specified by the configuration.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("max_participants")]
    public int? MaxParticipants { get; init; }

    /// <summary>
    /// Actual maximum number of concurrent participants in the conference.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("max_concurrent_participants")]
    public int? MaxConcurrentParticipants { get; init; }

    /// <summary>
    /// Unique conference participants based on caller ID.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("unique_participants")]
    public int? UniqueParticipants { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("end_reason")]
    public ConferenceEnumConferenceEndReason? EndReason { get; init; }

    /// <summary>
    /// Call SID of the participant whose actions ended the conference.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("ended_by")]
    [StringLength(34, MinimumLength = 34)]
    [RegularExpression("^CA[0-9a-fA-F]{32}$")]
    public string? EndedBy { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("mixer_region")]
    public ConferenceEnumRegion? MixerRegion { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("mixer_region_requested")]
    public ConferenceEnumRegion? MixerRegionRequested { get; init; }

    /// <summary>
    /// Boolean. Indicates whether recording was enabled at the conference mixer.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("recording_enabled")]
    public bool? RecordingEnabled { get; init; }

    /// <summary>
    /// Potential issues detected by Twilio during the conference.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("detected_issues")]
    public object? DetectedIssues { get; init; }

    /// <summary>
    /// Tags for detected conference conditions and participant behaviors which may be of interest.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("tags")]
    public IReadOnlyList<ConferenceEnumTag?>? Tags { get; init; }

    /// <summary>
    /// Object. Contains details about conference tags including severity.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("tag_info")]
    public object? TagInfo { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("processing_state")]
    public ConferenceEnumProcessingState? ProcessingState { get; init; }

    /// <summary>
    /// The URL of this resource.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("url")]
    [Format(FormatKind.Uri)]
    public string? Url { get; init; }

    /// <summary>
    /// Contains a dictionary of URL links to nested resources of this Conference.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("links")]
    public object? Links { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
