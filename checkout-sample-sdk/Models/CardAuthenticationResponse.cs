using System.Text.Json.Serialization;

namespace PayPalServerSdk.Models;

/// <summary>
/// Results of Authentication such as 3D Secure.
/// </summary>
public record CardAuthenticationResponse
{
    /// <summary>
    /// Results of 3D Secure Authentication.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("three_d_secure")]
    public ThreeDSecureCardAuthenticationResponse? ThreeDSecure { get; init; }
}
