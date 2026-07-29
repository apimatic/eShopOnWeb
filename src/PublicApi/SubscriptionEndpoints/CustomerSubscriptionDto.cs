using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class CustomerSubscriptionDto
{
    public int Id { get; set; }
    public string? PlanHandle { get; set; }
    public string? PlanName { get; set; }

    /// <summary>Subscription state, e.g. "active", "trialing", "canceled".</summary>
    public string? State { get; set; }

    public long? PriceInCents { get; set; }
    public decimal? Price { get; set; }
    public DateTimeOffset? NextBillingDate { get; set; }
}
