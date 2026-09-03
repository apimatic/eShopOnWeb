using System.Text.Json.Serialization;
using Maxio.Core.Models;

namespace Maxio.Models;

public record SubscriptionMigrationPreviewResponse
{
    [JsonPropertyName("migration")]
    public required SubscriptionMigrationPreview Migration { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
