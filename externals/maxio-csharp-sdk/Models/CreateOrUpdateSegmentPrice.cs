using System.Text.Json.Serialization;
using Maxio.Core.Models;
using Maxio.Models.AnyOf;

namespace Maxio.Models;

public record CreateOrUpdateSegmentPrice
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("starting_quantity")]
    public int? StartingQuantity { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("ending_quantity")]
    public int? EndingQuantity { get; init; }

    /// <summary>
    /// The price can contain up to 8 decimal places. e.g., 1.00 or 0.0012 or 0.00000065
    /// </summary>
    [JsonPropertyName("unit_price")]
    public required UnitPrice8 UnitPrice { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
