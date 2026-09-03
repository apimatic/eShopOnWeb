using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using TwilioSdk.Core.Models;
using TwilioSdk.Core.Validation;
using TwilioSdk.Core.Validation.Attributes;
using TwilioSdk.Models.Enums;

namespace TwilioSdk.Models;

public record VideoV1Room
{
    /// <summary>
    /// The unique string that Twilio created to identify the Room resource.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("sid")]
    [StringLength(34, MinimumLength = 34)]
    [RegularExpression("^RM[0-9a-fA-F]{32}$")]
    public string? Sid { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("status")]
    public RecordingTranscriptionEnumStatus? Status { get; init; }

    /// <summary>
    /// The date and time in GMT when the resource was created specified in <see href="https://en.wikipedia.org/wiki/ISO_8601">ISO 8601</see> format.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("date_created")]
    public DateTimeOffset? DateCreated { get; init; }

    /// <summary>
    /// The date and time in GMT when the resource was last updated specified in <see href="https://en.wikipedia.org/wiki/ISO_8601">ISO 8601</see> format.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("date_updated")]
    public DateTimeOffset? DateUpdated { get; init; }

    /// <summary>
    /// The SID of the <see href="https://www.twilio.com/docs/iam/api/account">Account</see> that created the Room resource.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("account_sid")]
    [StringLength(34, MinimumLength = 34)]
    [RegularExpression("^AC[0-9a-fA-F]{32}$")]
    public string? AccountSid { get; init; }

    /// <summary>
    /// Deprecated, now always considered to be true.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("enable_turn")]
    public bool? EnableTurn { get; init; }

    /// <summary>
    /// An application-defined string that uniquely identifies the resource. It can be used as a <c>room_sid</c> in place of the resource's <c>sid</c> in the URL to address the resource, assuming it does not contain any <see href="https://tools.ietf.org/html/rfc3986#section-2.2">reserved characters</see> that would need to be URL encoded. This value is unique for <c>in-progress</c> rooms. SDK clients can use this name to connect to the room. REST API clients can use this name in place of the Room SID to interact with the room as long as the room is <c>in-progress</c>.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("unique_name")]
    public string? UniqueName { get; init; }

    /// <summary>
    /// The URL Twilio calls using the <c>status_callback_method</c> to send status information to your application on every room event. See <see href="https://www.twilio.com/docs/video/api/status-callbacks">Status Callbacks</see> for more info.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("status_callback")]
    [Format(FormatKind.Uri)]
    public string? StatusCallback { get; init; }

    /// <summary>
    /// The HTTP method Twilio uses to call <c>status_callback</c>. Can be <c>POST</c> or <c>GET</c> and defaults to <c>POST</c>.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("status_callback_method")]
    public AmdStatusCallbackMethod? StatusCallbackMethod { get; init; }

    /// <summary>
    /// The UTC end time of the room in <see href="https://en.wikipedia.org/wiki/ISO_8601#UTC">ISO 8601</see> format.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("end_time")]
    public DateTimeOffset? EndTime { get; init; }

    /// <summary>
    /// The duration of the room in seconds.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("duration")]
    public int? Duration { get; init; }

    /// <summary>
    /// Type of room. Use <c>group</c> for new implementations. <c>go</c>, <c>peer-to-peer</c>, and <c>group-small</c> are deprecated.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("type")]
    public RoomEnumRoomType? Type { get; init; }

    /// <summary>
    /// The maximum number of concurrent Participants allowed in the room.
    /// </summary>
    [JsonPropertyName("max_participants")]
    public int? MaxParticipants { get; init; } = 0;

    /// <summary>
    /// The maximum number of seconds a Participant can be connected to the room. The maximum possible value is 86400 seconds (24 hours). The default is 14400 seconds (4 hours).
    /// </summary>
    [JsonPropertyName("max_participant_duration")]
    public int? MaxParticipantDuration { get; init; } = 0;

    /// <summary>
    /// The maximum number of published audio, video, and data tracks all participants combined are allowed to publish in the room at the same time. Check <see href="https://www.twilio.com/docs/video/programmable-video-limits">Programmable Video Limits</see> for more details. If it is set to 0 it means unconstrained.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("max_concurrent_published_tracks")]
    public int? MaxConcurrentPublishedTracks { get; init; }

    /// <summary>
    /// Whether to start recording when Participants connect.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("record_participants_on_connect")]
    public bool? RecordParticipantsOnConnect { get; init; }

    /// <summary>
    /// An array of the video codecs that are supported when publishing a track in the room.  Can be: <c>VP8</c> and <c>H264</c>.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("video_codecs")]
    public IReadOnlyList<RoomEnumVideoCodec?>? VideoCodecs { get; init; }

    /// <summary>
    /// The region for the Room's media server.  Can be one of the <see href="https://www.twilio.com/docs/video/ip-addresses#media-servers">available Media Regions</see>.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("media_region")]
    public string? MediaRegion { get; init; }

    /// <summary>
    /// When set to true, indicates that the participants in the room will only publish audio. No video tracks will be allowed.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("audio_only")]
    public bool? AudioOnly { get; init; }

    /// <summary>
    /// Specifies how long (in minutes) a room will remain active after last participant leaves. Can be configured when creating a room via REST API. For Ad-Hoc rooms this value cannot be changed.
    /// </summary>
    [JsonPropertyName("empty_room_timeout")]
    public int? EmptyRoomTimeout { get; init; } = 0;

    /// <summary>
    /// Specifies how long (in minutes) a room will remain active if no one joins. Can be configured when creating a room via REST API. For Ad-Hoc rooms this value cannot be changed.
    /// </summary>
    [JsonPropertyName("unused_room_timeout")]
    public int? UnusedRoomTimeout { get; init; } = 0;

    /// <summary>
    /// Indicates if this is a large room.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("large_room")]
    public bool? LargeRoom { get; init; }

    /// <summary>
    /// The absolute URL of the resource.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("url")]
    [Format(FormatKind.Uri)]
    public string? Url { get; init; }

    /// <summary>
    /// The URLs of related resources.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("links")]
    public object? Links { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
