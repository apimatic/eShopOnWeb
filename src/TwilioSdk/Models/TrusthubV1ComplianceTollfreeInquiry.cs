using System.Text.Json.Serialization;
using TwilioSdk.Core.Models;
using TwilioSdk.Core.Validation;
using TwilioSdk.Core.Validation.Attributes;

namespace TwilioSdk.Models;

public record TrusthubV1ComplianceTollfreeInquiry
{
    /// <summary>
    /// The unique ID used to start an embedded compliance registration session.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("inquiry_id")]
    public string? InquiryId { get; init; }

    /// <summary>
    /// The session token used to start an embedded compliance registration session.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("inquiry_session_token")]
    public string? InquirySessionToken { get; init; }

    /// <summary>
    /// The TolfreeId matching the Tollfree Profile that should be resumed or resubmitted for editing.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("registration_id")]
    public string? RegistrationId { get; init; }

    /// <summary>
    /// The URL of this resource.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("url")]
    [Format(FormatKind.Uri)]
    public string? Url { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
