using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using PayPalServerSdk.Models.Enums;

namespace PayPalServerSdk.Models;

/// <summary>
/// Results of 3D Secure Authentication.
/// </summary>
public record ThreeDSecureCardAuthenticationResponse
{
    /// <summary>
    /// Transactions status result identifier. The outcome of the issuer's authentication.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("authentication_status")]
    public ParesStatus? AuthenticationStatus { get; init; }

    /// <summary>
    /// Status of Authentication eligibility.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("enrollment_status")]
    public EnrollmentStatus? EnrollmentStatus { get; init; }

    /// <summary>
    /// The externally received 3ds authentication id, to be returned in card detokenization response.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("authentication_id")]
    [StringLength(255, MinimumLength = 1)]
    [RegularExpression("^[0-9a-zA-Z_-]+$")]
    public string? AuthenticationId { get; init; }
}
