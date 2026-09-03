using System.Text.Json.Serialization;
using Maxio.Core.Models;

namespace Maxio.Models;

public record SubscriptionProductChange
{
    [JsonPropertyName("previous_product_id")]
    public required int PreviousProductId { get; init; }

    [JsonPropertyName("new_product_id")]
    public required int NewProductId { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
