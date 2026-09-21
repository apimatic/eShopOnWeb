using System.Text.Json.Serialization;
using TwilioSdk.Core.Models;

namespace TwilioSdk.Models;

/// <summary>
/// Network-quality indicators for SDK and Twilio Gateway traffic during the report period.
/// </summary>
public record NetworkIssues
{
    /// <summary>
    /// Network issues of calls for client type. This is indicative of local network issues.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("sdk")]
    public Sdk? Sdk { get; init; }

    /// <summary>
    /// Network related metrics for Twilio Gateway calls only.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("twilio_gateway")]
    public TwilioGateway? TwilioGateway { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
