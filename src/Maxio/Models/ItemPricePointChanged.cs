using System.Text.Json.Serialization;
using Maxio.Core.Models;

namespace Maxio.Models;

public record ItemPricePointChanged
{
    [JsonPropertyName("item_id")]
    public required int ItemId { get; init; }

    [JsonPropertyName("item_type")]
    public required string ItemType { get; init; }

    [JsonPropertyName("item_handle")]
    public required string ItemHandle { get; init; }

    [JsonPropertyName("item_name")]
    public required string ItemName { get; init; }

    [JsonPropertyName("previous_price_point")]
    public required ItemPricePointData PreviousPricePoint { get; init; }

    [JsonPropertyName("current_price_point")]
    public required ItemPricePointData CurrentPricePoint { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
