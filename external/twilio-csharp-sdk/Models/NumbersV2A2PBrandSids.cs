using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Twilio.Core.Models;

namespace Twilio.Models;

public record NumbersV2A2PBrandSids
{
    /// <summary>
    /// Sid associated with campaign's brand
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("brandRegistrationSid")]
    [StringLength(34, MinimumLength = 34)]
    [RegularExpression("^BN[0-9a-f]{32}$")]
    public string? BrandRegistrationSid { get; init; }

    /// <summary>
    /// The external brand identifier (e.g., TCR Brand ID)
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("externalBrandId")]
    public string? ExternalBrandId { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
