using System.Text.Json.Serialization;
using Maxio.Core.Models;

namespace Maxio.Models;

public record ActivateEventBasedComponent
{
    /// <summary>
    /// The Chargify id of the price point
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("price_point_id")]
    public int? PricePointId { get; init; }

    /// <summary>
    /// Billing schedule settings for component allocations or usages on multi-frequency subscriptions. Use this to start a component's billing period on a custom date instead of aligning with the product charge schedule.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("billing_schedule")]
    public BillingSchedule? BillingSchedule { get; init; }

    /// <summary>
    /// Create or update custom pricing unique to the subscription. Used in place of <c>price_point_id</c>.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("custom_price")]
    public ComponentCustomPrice? CustomPrice { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
