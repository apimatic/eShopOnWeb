using System.Collections.Generic;
using System.Text.Json.Serialization;
using Twilio.Core.Models;

namespace Twilio.Models;

public record ListPluginReleaseResponse
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("releases")]
    public IReadOnlyList<FlexV1PluginRelease>? Releases { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("meta")]
    public Meta? Meta { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
