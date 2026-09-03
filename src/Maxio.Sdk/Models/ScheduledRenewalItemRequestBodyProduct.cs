using System.Text.Json.Serialization;
using Maxio.Models.Enums;

namespace Maxio.Models;

public record ScheduledRenewalItemRequestBodyProduct
{
    /// <summary>
    /// Item type to add. Either Product or Component.
    /// </summary>
    [JsonPropertyName("item_type")]
    public required ItemType1 ItemType { get; init; }

    /// <summary>
    /// Product or component identifier.
    /// </summary>
    [JsonPropertyName("item_id")]
    public required int ItemId { get; init; }

    /// <summary>
    /// Price point identifier.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("price_point_id")]
    public int? PricePointId { get; init; }

    /// <summary>
    /// (Optional) Quantity for the item.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("quantity")]
    public int? Quantity { get; init; }

    /// <summary>
    /// Custom pricing for a product within a scheduled renewal.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("custom_price")]
    public ScheduledRenewalProductPricePoint? CustomPrice { get; init; }
}
