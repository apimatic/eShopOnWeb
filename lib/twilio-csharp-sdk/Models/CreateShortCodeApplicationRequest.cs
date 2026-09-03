using System.Text.Json.Serialization;
using Twilio.Core.Models;

namespace Twilio.Models;

public record CreateShortCodeApplicationRequest
{
    /// <summary>
    /// The friendly name for the short code application.
    /// </summary>
    [JsonPropertyName("friendly_name")]
    public required string FriendlyName { get; init; }

    /// <summary>
    /// The ISO country code.
    /// </summary>
    [JsonPropertyName("iso_country")]
    public required string IsoCountry { get; init; }

    /// <summary>
    /// Business information associated with the application.
    /// </summary>
    [JsonPropertyName("business_information")]
    public required BusinessInformation BusinessInformation { get; init; }

    [JsonPropertyName("setup")]
    public required Setup Setup { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
