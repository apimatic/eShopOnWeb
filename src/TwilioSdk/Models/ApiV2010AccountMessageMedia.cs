using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using TwilioSdk.Core.Models;

namespace TwilioSdk.Models;

public record ApiV2010AccountMessageMedia
{
    /// <summary>
    /// The SID of the <see href="https://www.twilio.com/docs/iam/api/account">Account</see> associated with this Media resource.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("account_sid")]
    [StringLength(34, MinimumLength = 34)]
    [RegularExpression("^AC[0-9a-fA-F]{32}$")]
    public string? AccountSid { get; init; }

    /// <summary>
    /// The default <see href="https://en.wikipedia.org/wiki/Internet_media_type">MIME type</see> of the media, for example <c>image/jpeg</c>, <c>image/png</c>, or <c>image/gif</c>.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("content_type")]
    public string? ContentType { get; init; }

    /// <summary>
    /// The date and time in GMT when this Media resource was created, specified in <see href="https://www.ietf.org/rfc/rfc2822.txt">RFC 2822</see> format.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("date_created")]
    public string? DateCreated { get; init; }

    /// <summary>
    /// The date and time in GMT when this Media resource was last updated, specified in <see href="https://www.ietf.org/rfc/rfc2822.txt">RFC 2822</see> format.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("date_updated")]
    public string? DateUpdated { get; init; }

    /// <summary>
    /// The SID of the Message resource that is associated with this Media resource.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("parent_sid")]
    [StringLength(34, MinimumLength = 34)]
    [RegularExpression("^(SM|MM)[0-9a-fA-F]{32}$")]
    public string? ParentSid { get; init; }

    /// <summary>
    /// The unique string that identifies this Media resource.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("sid")]
    [StringLength(34, MinimumLength = 34)]
    [RegularExpression("^ME[0-9a-fA-F]{32}$")]
    public string? Sid { get; init; }

    /// <summary>
    /// The URI of this Media resource, relative to <c>https://api.twilio.com</c>.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("uri")]
    public string? Uri { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
