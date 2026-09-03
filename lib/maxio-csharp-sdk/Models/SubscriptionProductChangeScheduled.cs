using System;
using System.Text.Json.Serialization;
using Maxio.Core.Models;

namespace Maxio.Models;

public record SubscriptionProductChangeScheduled
{
    [JsonPropertyName("previous_product_id")]
    public required int PreviousProductId { get; init; }

    [JsonPropertyName("new_product_id")]
    public required int NewProductId { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("previous_product_price_point_id")]
    public int? PreviousProductPricePointId { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("new_product_price_point_id")]
    public int? NewProductPricePointId { get; init; }

    /// <summary>
    /// When the scheduled product change takes effect (the subscription's next renewal).
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("effective_at")]
    public DateTimeOffset? EffectiveAt { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
