using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// The caller's billing subscription: plan, price, state and next billing date.
/// </summary>
public class SubscriptionDto
{
    public int SubscriptionId { get; set; }
    public string PlanHandle { get; set; } = string.Empty;
    public string PlanName { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public long PriceInCents { get; set; }
    public DateTimeOffset? NextBillingDate { get; set; }
}