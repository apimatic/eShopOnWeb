using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Twilio.Core.Models;
using Twilio.Core.Validation;
using Twilio.Core.Validation.Attributes;

namespace Twilio.Models;

public record VideoV1CompositionSettings
{
    /// <summary>
    /// The SID of the <see href="https://www.twilio.com/docs/iam/api/account">Account</see> that created the CompositionSettings resource.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("account_sid")]
    [StringLength(34, MinimumLength = 34)]
    [RegularExpression("^AC[0-9a-fA-F]{32}$")]
    public string? AccountSid { get; init; }

    /// <summary>
    /// The string that you assigned to describe the resource and that will be shown in the console
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("friendly_name")]
    public string? FriendlyName { get; init; }

    /// <summary>
    /// The SID of the stored Credential resource.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("aws_credentials_sid")]
    [StringLength(34, MinimumLength = 34)]
    [RegularExpression("^CR[0-9a-fA-F]{32}$")]
    public string? AwsCredentialsSid { get; init; }

    /// <summary>
    /// The URL of the AWS S3 bucket where the compositions are stored. We only support DNS-compliant URLs like <c>https://documentation-example-twilio-bucket/compositions</c>, where <c>compositions</c> is the path in which you want the compositions to be stored. This URL accepts only URI-valid characters, as described in the <see href="https://tools.ietf.org/html/rfc3986#section-2">RFC 3986</see>.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("aws_s3_url")]
    [Format(FormatKind.Uri)]
    public string? AwsS3Url { get; init; }

    /// <summary>
    /// Whether all compositions are written to the <c>aws_s3_url</c>. When <c>false</c>, all compositions are stored in our cloud.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("aws_storage_enabled")]
    public bool? AwsStorageEnabled { get; init; }

    /// <summary>
    /// The SID of the Public Key resource used for encryption.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("encryption_key_sid")]
    [StringLength(34, MinimumLength = 34)]
    [RegularExpression("^CR[0-9a-fA-F]{32}$")]
    public string? EncryptionKeySid { get; init; }

    /// <summary>
    /// Whether all compositions are stored in an encrypted form. The default is <c>false</c>.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("encryption_enabled")]
    public bool? EncryptionEnabled { get; init; }

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
