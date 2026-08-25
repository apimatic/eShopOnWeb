using System.Text.Json.Serialization;
using PayPalServerSdk.Models.Enums;

namespace PayPalServerSdk.Models;

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
}
