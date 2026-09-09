using System.Text.Json.Serialization;
using MaxioAdvancedBilling.Core.Models;

namespace MaxioAdvancedBilling.Models;

public record SubscriptionProductMigrationRequest
{
    [JsonPropertyName("migration")]
    public required SubscriptionProductMigration Migration { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
