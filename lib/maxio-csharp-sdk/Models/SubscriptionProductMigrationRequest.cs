using System.Text.Json.Serialization;
using Maxio.Core.Models;

namespace Maxio.Models;

public record SubscriptionProductMigrationRequest
{
    [JsonPropertyName("migration")]
    public required SubscriptionProductMigration Migration { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
