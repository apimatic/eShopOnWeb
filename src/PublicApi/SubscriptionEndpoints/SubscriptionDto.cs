using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// A subscription in Maxio Advanced Billing, projected for API consumers.
/// </summary>
public class SubscriptionDto
{
    /// <summary>Maxio subscription id. Not stable across sandbox re-seeds.</summary>
    public int? Id { get; set; }

    /// <summary>Handle of the subscribed plan (stable), e.g. "eshop-pro".</summary>
    public string? PlanHandle { get; set; }

    public string? PlanName { get; set; }

    /// <summary>Wire value of the subscription state, e.g. "active", "trialing", "canceled".</summary>
    public string? State { get; set; }

    /// <summary>Recurring price in cents currently billed on this subscription.</summary>
    public long? PriceInCents { get; set; }

    /// <summary>Recurring price, derived from <see cref="PriceInCents"/>.</summary>
    public decimal Price => PriceInCents is { } cents ? cents / 100m : 0m;

    /// <summary>ISO currency code (e.g. "USD").</summary>
    public string? Currency { get; set; }

    public DateTimeOffset? CurrentPeriodStartedAt { get; set; }

    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }

    /// <summary>When the next payment is captured or retried.</summary>
    public DateTimeOffset? NextBillingDate { get; set; }
}
