using System.Text.Json.Serialization;
using Maxio.Core.Models;
using Maxio.Models.AnyOf;

namespace Maxio.Models;

public record SubscriptionGroupSignupComponent
{
    /// <summary>
    /// Required if passing any component to <c>components</c> attribute.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("component_id")]
    public ComponentId? ComponentId { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("allocated_quantity")]
    public AllocatedQuantity1? AllocatedQuantity { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("unit_balance")]
    public UnitBalance? UnitBalance { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("price_point_id")]
    public PricePointId? PricePointId { get; init; }

    /// <summary>
    /// Used in place of <c>price_point_id</c> to define a custom price point unique to the subscription. You still need to provide <c>component_id</c>.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("custom_price")]
    public SubscriptionGroupComponentCustomPrice? CustomPrice { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
