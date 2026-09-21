using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using TwilioSdk.Core.Models;
using TwilioSdk.Core.Validation;
using TwilioSdk.Core.Validation.Attributes;

namespace TwilioSdk.Models;

public record SyncV1ServiceDocumentDocumentPermission
{
    /// <summary>
    /// The SID of the <see href="https://www.twilio.com/docs/iam/api/account">Account</see> that created the Document Permission resource.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("account_sid")]
    [StringLength(34, MinimumLength = 34)]
    [RegularExpression("^AC[0-9a-fA-F]{32}$")]
    public string? AccountSid { get; init; }

    /// <summary>
    /// The SID of the <see href="https://www.twilio.com/docs/sync/api/service">Sync Service</see> the resource is associated with.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("service_sid")]
    [StringLength(34, MinimumLength = 34)]
    [RegularExpression("^IS[0-9a-fA-F]{32}$")]
    public string? ServiceSid { get; init; }

    /// <summary>
    /// The SID of the Sync Document to which the Document Permission applies.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("document_sid")]
    [StringLength(34, MinimumLength = 34)]
    [RegularExpression("^ET[0-9a-fA-F]{32}$")]
    public string? DocumentSid { get; init; }

    /// <summary>
    /// The application-defined string that uniquely identifies the resource's User within the Service to an FPA token.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("identity")]
    public string? Identity { get; init; }

    /// <summary>
    /// Whether the identity can read the Sync Document.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("read")]
    public bool? Read { get; init; }

    /// <summary>
    /// Whether the identity can update the Sync Document.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("write")]
    public bool? Write { get; init; }

    /// <summary>
    /// Whether the identity can delete the Sync Document.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("manage")]
    public bool? Manage { get; init; }

    /// <summary>
    /// The absolute URL of the Sync Document Permission resource.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("url")]
    [Format(FormatKind.Uri)]
    public string? Url { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
