using System.Collections.Generic;
using System.Text.Json.Serialization;
using Twilio.Core.Models;

namespace Twilio.Models;

public record ListSyncMapItemResponse
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("items")]
    public IReadOnlyList<SyncV1ServiceSyncMapSyncMapItem>? Items { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("meta")]
    public Meta? Meta { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
