using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using TwilioSdk.Core.Models;

namespace TwilioSdk.Models;

public record CreateShortCodeApplicationBundleInquiryRequest
{
    /// <summary>
    /// The unique identifier of the Short Code Application.
    /// </summary>
    [JsonPropertyName("application_sid")]
    [StringLength(34, MinimumLength = 34)]
    [RegularExpression("^WF[0-9a-fA-F]{32}$")]
    public required string ApplicationSid { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
