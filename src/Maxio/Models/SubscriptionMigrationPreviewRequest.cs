using System.Text.Json.Serialization;
using Maxio.Core.Models;

namespace Maxio.Models;

public record SubscriptionMigrationPreviewRequest
{
    [JsonPropertyName("migration")]
    public required SubscriptionMigrationPreviewOptions Migration { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
