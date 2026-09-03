using System.Collections.Generic;
using System.Text.Json.Serialization;
using Twilio.Core.Models;
using Twilio.Models.Enums;

namespace Twilio.Models;

public record MessagingV2RcsComplianceCountryResponse
{
    /// <summary>
    /// The ISO 3166-1 alpha-2 country code.
    /// </summary>
    [JsonPropertyName("country")]
    public required string Country { get; init; }

    /// <summary>
    /// The default compliance registration SID (e.g., from CR-Google) that applies to all countries unless overridden in the <c>countries</c> array.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("registration_sid")]
    public string? RegistrationSid { get; init; }

    /// <summary>
    /// The country-level status. Based on the aggregation of the carrier-level status.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("status")]
    public MessagingV2RcsCountryStatus? Status { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("carriers")]
    public IReadOnlyList<MessagingV2RcsCarrier>? Carriers { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
