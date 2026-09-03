using System.Text.Json.Serialization;
using PayPal.Core.Models;
using PayPal.Models.Enums;

namespace PayPal.Models;

/// <summary>
/// Results of Authentication such as 3D Secure.
/// </summary>
public record AuthenticationResponse
{
    /// <summary>
    /// Liability shift indicator. The outcome of the issuer's authentication.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("liability_shift")]
    public LiabilityShiftIndicator? LiabilityShift { get; init; }

    /// <summary>
    /// Results of 3D Secure Authentication.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("three_d_secure")]
    public ThreeDSecureAuthenticationResponse? ThreeDSecure { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
