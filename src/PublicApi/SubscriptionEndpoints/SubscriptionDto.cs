using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// A shopper's enrollment on a subscription plan, as reported by the billing provider.
/// </summary>
public class SubscriptionDto
{
    /// <summary>Identifier assigned by the billing provider.</summary>
    public int? Id { get; set; }

    /// <summary>The deterministic reference this application supplied at signup.</summary>
    public string? Reference { get; set; }

    /// <summary>Lifecycle state, for example "active".</summary>
    public string? State { get; set; }

    /// <summary>True while the subscription has not finally ended.</summary>
    public bool IsLive { get; set; }

    public string? PlanHandle { get; set; }

    public string? PlanName { get; set; }

    public long? PriceInCents { get; set; }

    public decimal? Price { get; set; }

    public int? Interval { get; set; }

    public string? IntervalUnit { get; set; }

    public DateTimeOffset? CurrentPeriodStartedAt { get; set; }

    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }

    /// <summary>When the billing provider will next bill this subscription.</summary>
    public DateTimeOffset? NextBillingAt { get; set; }

    public DateTimeOffset? ActivatedAt { get; set; }

    public DateTimeOffset? CanceledAt { get; set; }
}
