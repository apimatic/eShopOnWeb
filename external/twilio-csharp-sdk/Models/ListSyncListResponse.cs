using System.Collections.Generic;
using System.Text.Json.Serialization;
using Twilio.Core.Models;

namespace Twilio.Models;

public record ListSyncListResponse
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("lists")]
    public IReadOnlyList<SyncV1ServiceSyncList>? Lists { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("meta")]
    public Meta? Meta { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
