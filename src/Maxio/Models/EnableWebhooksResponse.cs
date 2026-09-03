using System.Text.Json.Serialization;
using Maxio.Core.Models;

namespace Maxio.Models;

public record EnableWebhooksResponse
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("webhooks_enabled")]
    public bool? WebhooksEnabled { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
