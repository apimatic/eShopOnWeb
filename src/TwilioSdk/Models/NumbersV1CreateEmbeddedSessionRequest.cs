using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using TwilioSdk.Core.Models;

namespace TwilioSdk.Models;

public record NumbersV1CreateEmbeddedSessionRequest
{
    /// <summary>
    /// Theme ID for the Compliance Embeddable UI. Overrides the theme set during registration creation.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("themeSetId")]
    [MaxLength(255)]
    public string? ThemeSetId { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
