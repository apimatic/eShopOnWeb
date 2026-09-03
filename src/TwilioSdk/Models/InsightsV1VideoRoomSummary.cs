using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using TwilioSdk.Core.Models;
using TwilioSdk.Core.Validation;
using TwilioSdk.Core.Validation.Attributes;
using TwilioSdk.Models.Enums;

namespace TwilioSdk.Models;

public record InsightsV1VideoRoomSummary
{
    /// <summary>
    /// Account SID associated with this room.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("account_sid")]
    [StringLength(34, MinimumLength = 34)]
    [RegularExpression("^AC[0-9a-fA-F]{32}$")]
    public string? AccountSid { get; init; }

    /// <summary>
    /// Unique identifier for the room.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("room_sid")]
    [StringLength(34, MinimumLength = 34)]
    [RegularExpression("^RM[0-9a-fA-F]{32}$")]
    public string? RoomSid { get; init; }

    /// <summary>
    /// Room friendly name.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("room_name")]
    public string? RoomName { get; init; }

    /// <summary>
    /// Creation time of the room.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("create_time")]
    public DateTimeOffset? CreateTime { get; init; }

    /// <summary>
    /// End time for the room.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("end_time")]
    public DateTimeOffset? EndTime { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("room_type")]
    public VideoRoomSummaryEnumRoomType? RoomType { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("room_status")]
    public VideoRoomSummaryEnumRoomStatus? RoomStatus { get; init; }

    /// <summary>
    /// Webhook provided for status callbacks.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("status_callback")]
    [Format(FormatKind.Uri)]
    public string? StatusCallback { get; init; }

    /// <summary>
    /// HTTP method provided for status callback URL.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("status_callback_method")]
    public AmdStatusCallbackMethod? StatusCallbackMethod { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("created_method")]
    public VideoRoomSummaryEnumCreatedMethod? CreatedMethod { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("end_reason")]
    public VideoRoomSummaryEnumEndReason? EndReason { get; init; }

    /// <summary>
    /// Max number of total participants allowed by the application settings.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("max_participants")]
    public int? MaxParticipants { get; init; }

    /// <summary>
    /// Number of participants. May include duplicate identities for participants who left and rejoined.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("unique_participants")]
    public int? UniqueParticipants { get; init; }

    /// <summary>
    /// Unique number of participant identities.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("unique_participant_identities")]
    public int? UniqueParticipantIdentities { get; init; }

    /// <summary>
    /// Actual number of concurrent participants.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("concurrent_participants")]
    public int? ConcurrentParticipants { get; init; }

    /// <summary>
    /// Maximum number of participants allowed in the room at the same time allowed by the application settings.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("max_concurrent_participants")]
    public int? MaxConcurrentParticipants { get; init; }

    /// <summary>
    /// Codecs used by participants in the room. Can be <c>VP8</c>, <c>H264</c>, or <c>VP9</c>.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("codecs")]
    public IReadOnlyList<VideoRoomSummaryEnumCodec?>? Codecs { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("media_region")]
    public VideoRoomSummaryEnumTwilioRealm? MediaRegion { get; init; }

    /// <summary>
    /// Total room duration from create time to end time.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("duration_sec")]
    public long? DurationSec { get; init; }

    /// <summary>
    /// Combined amount of participant time in the room.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("total_participant_duration_sec")]
    public long? TotalParticipantDurationSec { get; init; }

    /// <summary>
    /// Combined amount of recorded seconds for participants in the room.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("total_recording_duration_sec")]
    public long? TotalRecordingDurationSec { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("processing_state")]
    public VideoRoomSummaryEnumProcessingState? ProcessingState { get; init; }

    /// <summary>
    /// Boolean indicating if recording is enabled for the room.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("recording_enabled")]
    public bool? RecordingEnabled { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("edge_location")]
    public VideoRoomSummaryEnumEdgeLocation? EdgeLocation { get; init; }

    /// <summary>
    /// URL for the room resource.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("url")]
    [Format(FormatKind.Uri)]
    public string? Url { get; init; }

    /// <summary>
    /// Room subresources.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("links")]
    public object? Links { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
