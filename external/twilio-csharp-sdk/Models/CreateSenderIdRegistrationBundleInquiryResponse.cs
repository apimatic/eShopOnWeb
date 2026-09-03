using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Twilio.Core.Models;

namespace Twilio.Models;

public record CreateSenderIdRegistrationBundleInquiryResponse
{
    /// <summary>
    /// The unique identifier of the inquiry.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("inquiry_id")]
    public string? InquiryId { get; init; }

    /// <summary>
    /// The session token for the inquiry.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("inquiry_session_token")]
    public string? InquirySessionToken { get; init; }

    /// <summary>
    /// The unique identifier of the Sender ID Registration Application.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("application_sid")]
    [StringLength(34, MinimumLength = 34)]
    [RegularExpression("^WF[0-9a-fA-F]{32}$")]
    public string? ApplicationSid { get; init; }

    /// <summary>
    /// The Bundle SID associated with the inquiry.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("bundle_sid")]
    [StringLength(34, MinimumLength = 34)]
    [RegularExpression("^BU[0-9a-fA-F]{32}$")]
    public string? BundleSid { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
