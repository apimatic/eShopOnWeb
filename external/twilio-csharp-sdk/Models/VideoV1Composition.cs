using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Twilio.Core.Models;
using Twilio.Core.Validation;
using Twilio.Core.Validation.Attributes;
using Twilio.Models.Enums;

namespace Twilio.Models;

public record VideoV1Composition
{
    /// <summary>
    /// The SID of the <see href="https://www.twilio.com/docs/iam/api/account">Account</see> that created the Composition resource.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("account_sid")]
    [StringLength(34, MinimumLength = 34)]
    [RegularExpression("^AC[0-9a-fA-F]{32}$")]
    public string? AccountSid { get; init; }

    /// <summary>
    /// The status of the composition. Can be: <c>enqueued</c>, <c>processing</c>, <c>completed</c>, <c>deleted</c> or <c>failed</c>. <c>enqueued</c> is the initial state and indicates that the composition request has been received and is scheduled for processing; <c>processing</c> indicates the composition is being processed; <c>completed</c> indicates the composition has been completed and is available for download; <c>deleted</c> means the composition media has been deleted from the system, but its metadata is still available for 30 days; <c>failed</c> indicates the composition failed to execute the media processing task.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("status")]
    public CompositionEnumStatus? Status { get; init; }

    /// <summary>
    /// The date and time in GMT when the resource was created specified in <see href="https://en.wikipedia.org/wiki/ISO_8601">ISO 8601</see> format.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("date_created")]
    public DateTimeOffset? DateCreated { get; init; }

    /// <summary>
    /// The date and time in GMT when the composition's media processing task finished, specified in <see href="https://en.wikipedia.org/wiki/ISO_8601">ISO 8601</see> format.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("date_completed")]
    public DateTimeOffset? DateCompleted { get; init; }

    /// <summary>
    /// The date and time in GMT when the composition generated media was deleted, specified in <see href="https://en.wikipedia.org/wiki/ISO_8601">ISO 8601</see> format.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("date_deleted")]
    public DateTimeOffset? DateDeleted { get; init; }

    /// <summary>
    /// The unique string that we created to identify the Composition resource.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("sid")]
    [StringLength(34, MinimumLength = 34)]
    [RegularExpression("^CJ[0-9a-fA-F]{32}$")]
    public string? Sid { get; init; }

    /// <summary>
    /// The SID of the Group Room that generated the audio and video tracks used in the composition. All media sources included in a composition must belong to the same Group Room.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("room_sid")]
    [StringLength(34, MinimumLength = 34)]
    [RegularExpression("^RM[0-9a-fA-F]{32}$")]
    public string? RoomSid { get; init; }

    /// <summary>
    /// The array of track names to include in the composition. The composition includes all audio sources specified in <c>audio_sources</c> except those specified in <c>audio_sources_excluded</c>. The track names in this property can include an asterisk as a wild card character, which matches zero or more characters in a track name. For example, <c>student*</c> includes tracks named <c>student</c> as well as <c>studentTeam</c>.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("audio_sources")]
    public IReadOnlyList<string?>? AudioSources { get; init; }

    /// <summary>
    /// The array of track names to exclude from the composition. The composition includes all audio sources specified in <c>audio_sources</c> except for those specified in <c>audio_sources_excluded</c>. The track names in this property can include an asterisk as a wild card character, which matches zero or more characters in a track name. For example, <c>student*</c> excludes <c>student</c> as well as <c>studentTeam</c>. This parameter can also be empty.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("audio_sources_excluded")]
    public IReadOnlyList<string?>? AudioSourcesExcluded { get; init; }

    /// <summary>
    /// An object that describes the video layout of the composition in terms of regions. See <see href="https://www.twilio.com/docs/video/api/compositions-resource#specifying-video-layouts">Specifying Video Layouts</see> for more info.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("video_layout")]
    public object? VideoLayout { get; init; }

    /// <summary>
    /// The dimensions of the video image in pixels expressed as columns (width) and rows (height). The string's format is <c>{width}x{height}</c>, such as <c>640x480</c>.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("resolution")]
    public string? Resolution { get; init; }

    /// <summary>
    /// Whether to remove intervals with no media, as specified in the POST request that created the composition. Compositions with <c>trim</c> enabled are shorter when the Room is created and no Participant joins for a while as well as if all the Participants leave the room and join later, because those gaps will be removed. See <see href="https://www.twilio.com/docs/video/api/compositions-resource#specifying-video-layouts">Specifying Video Layouts</see> for more info.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("trim")]
    public bool? Trim { get; init; }

    /// <summary>
    /// The container format of the composition's media files as specified in the POST request that created the Composition resource. See <see href="https://www.twilio.com/docs/video/api/compositions-resource#http-post-parameters">POST Parameters</see> for more information.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("format")]
    public CompositionEnumFormat? Format { get; init; }

    /// <summary>
    /// The average bit rate of the composition's media.
    /// </summary>
    [JsonPropertyName("bitrate")]
    public int? Bitrate { get; init; } = 0;

    /// <summary>
    /// The size of the composed media file in bytes.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("size")]
    public long? Size { get; init; }

    /// <summary>
    /// The duration of the composition's media file in seconds.
    /// </summary>
    [JsonPropertyName("duration")]
    public int? Duration { get; init; } = 0;

    /// <summary>
    /// The URL of the media file associated with the composition when stored externally. See <see href="/docs/video/api/external-s3-compositions">External S3 Compositions</see> for more details.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("media_external_location")]
    [Format(FormatKind.Uri)]
    public string? MediaExternalLocation { get; init; }

    /// <summary>
    /// The URL called using the <c>status_callback_method</c> to send status information on every composition event.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("status_callback")]
    [Format(FormatKind.Uri)]
    public string? StatusCallback { get; init; }

    /// <summary>
    /// The HTTP method used to call <c>status_callback</c>. Can be: <c>POST</c> or <c>GET</c>, defaults to <c>POST</c>.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("status_callback_method")]
    public AmdStatusCallbackMethod? StatusCallbackMethod { get; init; }

    /// <summary>
    /// The absolute URL of the resource.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("url")]
    [Format(FormatKind.Uri)]
    public string? Url { get; init; }

    /// <summary>
    /// The URL of the media file associated with the composition.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("links")]
    public object? Links { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
