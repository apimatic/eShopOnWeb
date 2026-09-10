using System.Text.Json.Serialization;
using MaxioAdvancedBilling.Core.Models;

namespace MaxioAdvancedBilling.Models;

public record SubscriptionMigrationPreviewRequest
{
    [JsonPropertyName("migration")]
    public required SubscriptionMigrationPreviewOptions Migration { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
