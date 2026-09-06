using System.Text.Json.Serialization;
using Microsoft.eShopWeb.PublicApi.Services;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscriptionPlanDto
{
    [JsonPropertyName("handle")]
    public string Handle { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("price")]
    public decimal Price { get; set; }

    [JsonPropertyName("intervalUnit")]
    public string IntervalUnit { get; set; } = string.Empty;

    [JsonPropertyName("interval")]
    public int Interval { get; set; }

    public static SubscriptionPlanDto FromPlan(SubscriptionPlan plan)
    {
        return new SubscriptionPlanDto
        {
            Handle = plan.Handle,
            Name = plan.Name,
            Description = plan.Description,
            Price = plan.PriceInCents / 100m,
            IntervalUnit = plan.IntervalUnit,
            Interval = plan.Interval
        };
    }
}
