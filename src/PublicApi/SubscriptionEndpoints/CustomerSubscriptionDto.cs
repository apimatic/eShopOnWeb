using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>A shopper's subscription as reflected in Maxio.</summary>
public class CustomerSubscriptionDto
{
    public int Id { get; set; }
    public string? PlanHandle { get; set; }
    public string? PlanName { get; set; }

    /// <summary>Current subscription state (e.g. "active", "trialing").</summary>
    public string? State { get; set; }

    public long? PriceInCents { get; set; }

    /// <summary>Current price in major units (cents / 100), for display.</summary>
    public decimal? Price { get; set; }

    /// <summary>When Maxio will next bill this subscription.</summary>
    public DateTimeOffset? NextBillingAt { get; set; }

    public string? Reference { get; set; }
}
