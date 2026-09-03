using System.Text.Json.Serialization;
using Maxio.Core.Models;

namespace Maxio.Models;

public record SubscriptionPreviewResponse
{
    [JsonPropertyName("subscription_preview")]
    public required SubscriptionPreview SubscriptionPreview { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
