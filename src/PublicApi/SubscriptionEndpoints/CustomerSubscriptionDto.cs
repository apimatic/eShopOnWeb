using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class CustomerSubscriptionDto
{
    public int Id { get; set; }
    public string? PlanHandle { get; set; }
    public string? PlanName { get; set; }

    /// <summary>Subscription state as reported by the billing provider (e.g. "active", "trialing").</summary>
    public string State { get; set; } = string.Empty;

    public long? PriceInCents { get; set; }
    public decimal? Price { get; set; }

    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }

    /// <summary>The date the next billing/assessment is expected.</summary>
    public DateTimeOffset? NextBillingDate { get; set; }
}
