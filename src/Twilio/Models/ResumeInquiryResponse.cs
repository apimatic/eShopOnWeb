using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Twilio.Core.Models;

namespace Twilio.Models;

public record ResumeInquiryResponse
{
    /// <summary>
    /// Persona inquiry ID (existing or new)
    /// </summary>
    [JsonPropertyName("inquiry_id")]
    public required string InquiryId { get; init; }

    /// <summary>
    /// Persona session token (always new, expires in 24 hours)
    /// </summary>
    [JsonPropertyName("inquiry_session_token")]
    public required string InquirySessionToken { get; init; }

    /// <summary>
    /// Bundle SID
    /// </summary>
    [JsonPropertyName("bundle_sid")]
    [StringLength(34, MinimumLength = 34)]
    [RegularExpression("^BU[0-9a-fA-F]{32}$")]
    public required string BundleSid { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
