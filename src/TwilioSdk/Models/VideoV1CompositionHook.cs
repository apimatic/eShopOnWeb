using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using TwilioSdk.Core.Models;
using TwilioSdk.Core.Validation;
using TwilioSdk.Core.Validation.Attributes;
using TwilioSdk.Models.Enums;

namespace TwilioSdk.Models;

public record VideoV1CompositionHook
{
    /// <summary>
    /// The SID of the <see href="https://www.twilio.com/docs/iam/api/account">Account</see> that created the CompositionHook resource.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("account_sid")]
    [StringLength(34, MinimumLength = 34)]
    [RegularExpression("^AC[0-9a-fA-F]{32}$")]
    public string? AccountSid { get; init; }

    /// <summary>
    /// The string that you assigned to describe the resource. Can be up to 100 characters long and must be unique within the account.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("friendly_name")]
    public string? FriendlyName { get; init; }

    /// <summary>
    /// Whether the CompositionHook is active. When <c>true</c>, the CompositionHook is triggered for every completed Group Room on the account. When <c>false</c>, the CompositionHook is never triggered.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("enabled")]
    public bool? Enabled { get; init; }

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
    /// The unique string that we created to identify the CompositionHook resource.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("sid")]
    [StringLength(34, MinimumLength = 34)]
    [RegularExpression("^HK[0-9a-fA-F]{32}$")]
    public string? Sid { get; init; }

    /// <summary>
    /// The array of track names to include in the compositions created by the composition hook. A composition triggered by the composition hook includes all audio sources specified in <c>audio_sources</c> except those specified in <c>audio_sources_excluded</c>. The track names in this property can include an asterisk as a wild card character, which matches zero or more characters in a track name. For example, <c>student*</c> includes tracks named <c>student</c> as well as <c>studentTeam</c>. Please, be aware that either video_layout or audio_sources have to be provided to get a valid creation request
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("audio_sources")]
    public IReadOnlyList<string?>? AudioSources { get; init; }

    /// <summary>
    /// The array of track names to exclude from the compositions created by the composition hook. A composition triggered by the composition hook includes all audio sources specified in <c>audio_sources</c> except for those specified in <c>audio_sources_excluded</c>. The track names in this property can include an asterisk as a wild card character, which matches zero or more characters in a track name. For example, <c>student*</c> excludes <c>student</c> as well as <c>studentTeam</c>. This parameter can also be empty.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("audio_sources_excluded")]
    public IReadOnlyList<string?>? AudioSourcesExcluded { get; init; }

    /// <summary>
    /// A JSON object that describes the video layout of the composition in terms of regions as specified in the HTTP POST request that created the CompositionHook resource. See <see href="https://www.twilio.com/docs/video/api/compositions-resource#http-post-parameters">POST Parameters</see> for more information. Please, be aware that either video_layout or audio_sources have to be provided to get a valid creation request
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
    /// Whether intervals with no media are clipped, as specified in the POST request that created the CompositionHook resource. Compositions with <c>trim</c> enabled are shorter when the Room is created and no Participant joins for a while as well as if all the Participants leave the room and join later, because those gaps will be removed. See <see href="https://www.twilio.com/docs/video/api/compositions-resource#specifying-video-layouts">Specifying Video Layouts</see> for more info.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("trim")]
    public bool? Trim { get; init; }

    /// <summary>
    /// The container format of the media files used by the compositions created by the composition hook. If <c>mp4</c> or <c>webm</c>, <c>audio_sources</c> must have one or more tracks and/or a <c>video_layout</c> element must contain a valid <c>video_sources</c> list, otherwise an error occurs.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("format")]
    public CompositionHookEnumFormat? Format { get; init; }

    /// <summary>
    /// The URL we call using the <c>status_callback_method</c> to send status information to your application.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("status_callback")]
    [Format(FormatKind.Uri)]
    public string? StatusCallback { get; init; }

    /// <summary>
    /// The HTTP method we should use to call <c>status_callback</c>. Can be <c>POST</c> or <c>GET</c> and defaults to <c>POST</c>.
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

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
