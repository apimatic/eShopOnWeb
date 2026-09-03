using System.Text.Json.Serialization;
using Maxio.Core.Models;
using Maxio.Models.AnyOf;

namespace Maxio.Models;

public record RenewalPreviewComponent
{
    /// <summary>
    /// Either the component's Chargify id or its handle prefixed with <c>handle:</c>
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("component_id")]
    public ComponentId2? ComponentId { get; init; }

    /// <summary>
    /// The quantity for which you wish to preview billing. This is useful if you want to preview a predicted, higher usage value than is currently present on the subscription.
    /// <para>
    /// This quantity represents:
    /// </para>
    /// <list type="bullet">
    ///   <item><description>Whether or not an on/off component is enabled - use 0 for disabled or 1 for enabled</description></item>
    ///   <item><description>The desired allocated_quantity for a quantity-based component</description></item>
    ///   <item><description>The desired unit_balance for a metered component</description></item>
    ///   <item><description>The desired metric quantity for an events-based component</description></item>
    /// </list>
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("quantity")]
    public int? Quantity { get; init; }

    /// <summary>
    /// Either the component price point's Chargify id or its handle prefixed with <c>handle:</c>
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("price_point_id")]
    public PricePointId3? PricePointId { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
