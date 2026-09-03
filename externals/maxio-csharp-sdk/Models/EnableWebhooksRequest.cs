using System.Text.Json.Serialization;
using Maxio.Core.Models;

namespace Maxio.Models;

public record EnableWebhooksRequest
{
    [JsonPropertyName("webhooks_enabled")]
    public required bool WebhooksEnabled { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
