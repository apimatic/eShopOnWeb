using System.Collections.Generic;
using System.Text.Json.Serialization;
using TwilioSdk.Core.Models;

namespace TwilioSdk.Models;

public record ConfigurationEvent
{
    /// <summary>
    /// Key-value pairs for configuration settings.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("configurations")]
    public IReadOnlyDictionary<string, string>? Configurations { get; init; }

    /// <summary>
    /// Key-value pairs for language configurations.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("languages")]
    public IReadOnlyDictionary<string, Languages>? Languages { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
