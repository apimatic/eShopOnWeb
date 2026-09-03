using System.Collections.Generic;
using System.Text.Json.Serialization;
using Twilio.Core.Models;

namespace Twilio.Models;

public record LocaleResponse
{
    /// <summary>
    /// List of supported languages for opt-out configurations
    /// </summary>
    [JsonPropertyName("languages")]
    public required IReadOnlyList<LanguageProperties> Languages { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
