using System;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Twilio.Core.Models;
using Twilio.Core.Validation;
using Twilio.Core.Validation.Attributes;
using Twilio.Models.Enums;

namespace Twilio.Models;

public record VideoV1Recording
{
    /// <summary>
    /// The SID of the <see href="https://www.twilio.com/docs/iam/api/account">Account</see> that created the Recording resource.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("account_sid")]
    [StringLength(34, MinimumLength = 34)]
    [RegularExpression("^AC[0-9a-fA-F]{32}$")]
    public string? AccountSid { get; init; }

    /// <summary>
    /// The status of the recording. Can be: <c>processing</c>, <c>completed</c>, or <c>deleted</c>. <c>processing</c> indicates the recording is still being captured; <c>completed</c> indicates the recording has been captured and is now available for download. <c>deleted</c> means the recording media has been deleted from the system, but its metadata is still available.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("status")]
    public RecordingEnumStatus1? Status { get; init; }

    /// <summary>
    /// The date and time in GMT when the resource was created specified in <see href="https://en.wikipedia.org/wiki/ISO_8601">ISO 8601</see> format.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("date_created")]
    public DateTimeOffset? DateCreated { get; init; }

    /// <summary>
    /// The unique string that we created to identify the Recording resource.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("sid")]
    [StringLength(34, MinimumLength = 34)]
    [RegularExpression("^RT[0-9a-fA-F]{32}$")]
    public string? Sid { get; init; }

    /// <summary>
    /// The SID of the recording source. For a Room Recording, this value is a <c>track_sid</c>.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("source_sid")]
    [StringLength(34, MinimumLength = 34)]
    [RegularExpression("^[a-zA-Z]{2}[0-9a-fA-F]{32}$")]
    public string? SourceSid { get; init; }

    /// <summary>
    /// The size of the recorded track, in bytes.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("size")]
    public long? Size { get; init; }

    /// <summary>
    /// The absolute URL of the resource.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("url")]
    [Format(FormatKind.Uri)]
    public string? Url { get; init; }

    /// <summary>
    /// The recording's media type. Can be: <c>audio</c> or <c>video</c>.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("type")]
    public RecordingEnumType? Type { get; init; }

    /// <summary>
    /// The duration of the recording in seconds rounded to the nearest second. Sub-second tracks have a <c>Duration</c> property of 1 second
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("duration")]
    public int? Duration { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("container_format")]
    public RecordingEnumFormat? ContainerFormat { get; init; }

    /// <summary>
    /// The codec used to encode the track. Can be: <c>VP8</c>, <c>H264</c>, <c>OPUS</c>, and <c>PCMU</c>.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("codec")]
    public RecordingEnumCodec? Codec { get; init; }

    /// <summary>
    /// A list of SIDs related to the recording. Includes the <c>room_sid</c> and <c>participant_sid</c>.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("grouping_sids")]
    public object? GroupingSids { get; init; }

    /// <summary>
    /// The name that was given to the source track of the recording. If no name is given, the <c>source_sid</c> is used.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("track_name")]
    public string? TrackName { get; init; }

    /// <summary>
    /// The time in milliseconds elapsed between an arbitrary point in time, common to all group rooms, and the moment when the source room of this track started. This information provides a synchronization mechanism for recordings belonging to the same room.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("offset")]
    public long? Offset { get; init; }

    /// <summary>
    /// The URL of the media file associated with the recording when stored externally. See <see href="/docs/video/api/external-s3-recordings">External S3 Recordings</see> for more details.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("media_external_location")]
    [Format(FormatKind.Uri)]
    public string? MediaExternalLocation { get; init; }

    /// <summary>
    /// The URL called using the <c>status_callback_method</c> to send status information on every recording event.
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
    /// The URLs of related resources.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("links")]
    public object? Links { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
