using System.Text.Json.Serialization;
using TwilioSdk.Core.Models;

namespace TwilioSdk.Models;

/// <summary>
/// Business information associated with the application.
/// </summary>
public record BusinessInformation
{
    /// <summary>
    /// The Compliance Profile SID for the customer-facing business profile.
    /// </summary>
    [JsonPropertyName("customer_facing_profile")]
    public required string CustomerFacingProfile { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
