using System.Text.Json.Serialization;
using Maxio.Core.Models;

namespace Maxio.Models;

/// <summary>
/// Updatable fields for Subscription Note
/// </summary>
public record UpdateSubscriptionNote
{
    [JsonPropertyName("body")]
    public required string Body { get; init; }

    [JsonPropertyName("sticky")]
    public required bool Sticky { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
