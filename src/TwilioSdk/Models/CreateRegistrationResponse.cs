using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using TwilioSdk.Core.Models;

namespace TwilioSdk.Models;

public record CreateRegistrationResponse
{
    /// <summary>
    /// Bundle SID (same as bundle_sid in KYC Orchestration)
    /// </summary>
    [JsonPropertyName("bundle_sid")]
    [StringLength(34, MinimumLength = 34)]
    [RegularExpression("^BU[0-9a-fA-F]{32}$")]
    public required string BundleSid { get; init; }

    /// <summary>
    /// Persona inquiry ID
    /// </summary>
    [JsonPropertyName("inquiry_id")]
    public required string InquiryId { get; init; }

    /// <summary>
    /// Persona session token for embedding Persona UI
    /// </summary>
    [JsonPropertyName("inquiry_session_token")]
    public required string InquirySessionToken { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
