using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// A customer's subscription as confirmed back to the API client, including the plan, price, current
/// state, and the next billing date (the provider's current-period-end).
/// </summary>
public class SubscriptionDto
{
    public int SubscriptionId { get; set; }
    public string State { get; set; } = string.Empty;
    public string? PlanHandle { get; set; }
    public string? PlanName { get; set; }
    public long PriceInCents { get; set; }
    public decimal Price { get; set; }
    public string PriceDisplay { get; set; } = string.Empty;
    public DateTimeOffset? NextBillingDate { get; set; }
}
