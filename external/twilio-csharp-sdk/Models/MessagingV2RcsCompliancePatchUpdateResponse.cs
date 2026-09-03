using System.Collections.Generic;
using System.Text.Json.Serialization;
using Twilio.Core.Models;

namespace Twilio.Models;

/// <summary>
/// The KYC compliance information. This section consists of response to the request launch.
/// </summary>
public record MessagingV2RcsCompliancePatchUpdateResponse
{
    /// <summary>
    /// The default compliance registration SID (e.g., from CR-Google) that applies to all countries unless overridden within the <c>countries</c> array.
    /// </summary>
    [JsonPropertyName("registration_sid")]
    public required string RegistrationSid { get; init; }

    /// <summary>
    /// A list of country-specific compliance details.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("countries")]
    public IReadOnlyList<MessagingV2RcsComplianceCountryUpdatePatchResponse>? Countries { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
