using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Twilio.Core.Models;
using Twilio.Core.Validation;
using Twilio.Core.Validation.Attributes;
using Twilio.Models.Enums;

namespace Twilio.Models;

public record InsightsV1VideoRoomSummaryVideoParticipantSummary
{
    /// <summary>
    /// Unique identifier for the participant.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("participant_sid")]
    [StringLength(34, MinimumLength = 34)]
    [RegularExpression("^PA[0-9a-fA-F]{32}$")]
    public string? ParticipantSid { get; init; }

    /// <summary>
    /// The application-defined string that uniquely identifies the participant within a Room.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("participant_identity")]
    public string? ParticipantIdentity { get; init; }

    /// <summary>
    /// When the participant joined the room.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("join_time")]
    public DateTimeOffset? JoinTime { get; init; }

    /// <summary>
    /// When the participant left the room.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("leave_time")]
    public DateTimeOffset? LeaveTime { get; init; }

    /// <summary>
    /// Amount of time in seconds the participant was in the room.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("duration_sec")]
    public long? DurationSec { get; init; }

    /// <summary>
    /// Account SID associated with the room.
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

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("status")]
    public VideoParticipantSummaryEnumRoomStatus? Status { get; init; }

    /// <summary>
    /// Codecs detected from the participant. Can be <c>VP8</c>, <c>H264</c>, or <c>VP9</c>.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("codecs")]
    public IReadOnlyList<VideoParticipantSummaryEnumCodec?>? Codecs { get; init; }

    /// <summary>
    /// Reason the participant left the room. See <see href="https://www.twilio.com/docs/video/troubleshooting/video-log-analyzer-api#end_reason">the list of possible values here</see>.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("end_reason")]
    public string? EndReason { get; init; }

    /// <summary>
    /// Errors encountered by the participant.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("error_code")]
    public int? ErrorCode { get; init; }

    /// <summary>
    /// Twilio error code dictionary link.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("error_code_url")]
    public string? ErrorCodeUrl { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("media_region")]
    public VideoParticipantSummaryEnumTwilioRealm? MediaRegion { get; init; }

    /// <summary>
    /// Object containing information about the participant's data from the room. See <see href="https://www.twilio.com/docs/video/troubleshooting/video-log-analyzer-api#properties">below</see> for more information.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("properties")]
    public object? Properties { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("edge_location")]
    public VideoParticipantSummaryEnumEdgeLocation? EdgeLocation { get; init; }

    /// <summary>
    /// Object containing information about the SDK name and version. See <see href="https://www.twilio.com/docs/video/troubleshooting/video-log-analyzer-api#publisher_info">below</see> for more information.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("publisher_info")]
    public object? PublisherInfo { get; init; }

    /// <summary>
    /// URL of the participant resource.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("url")]
    [Format(FormatKind.Uri)]
    public string? Url { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
