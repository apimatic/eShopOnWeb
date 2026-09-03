using System.Collections.Generic;
using System.Text.Json.Serialization;
using Twilio.Core.Models;

namespace Twilio.Models;

public record ListSyncStreamResponse
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("streams")]
    public IReadOnlyList<SyncV1ServiceSyncStream>? Streams { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("meta")]
    public Meta? Meta { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
