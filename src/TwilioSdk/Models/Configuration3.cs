using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using TwilioSdk.Core.Models;

namespace TwilioSdk.Models;

/// <summary>
/// Conversation configuration settings.
/// </summary>
public record Configuration3
{
    /// <summary>
    /// A list of Conversational Intelligence configuration IDs.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("intelligenceConfigurationIds")]
    [MaxLength(5)]
    public IReadOnlyList<string>? IntelligenceConfigurationIds { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
