using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionBilling;

/// <summary>
/// A shopper's subscription as reflected by Maxio Advanced Billing (the billing system of record).
/// Transport-neutral projection of a Maxio subscription.
/// </summary>
public class SubscriptionInfo
{
    /// <summary>Maxio subscription id.</summary>
    public int? SubscriptionId { get; init; }

    /// <summary>Handle of the subscribed plan/product.</summary>
    public string? PlanHandle { get; init; }

    /// <summary>Name of the subscribed plan/product.</summary>
    public string? PlanName { get; init; }

    /// <summary>Subscription state wire value (e.g. <c>active</c>, <c>trialing</c>).</summary>
    public string? State { get; init; }

    /// <summary>Recurring price in integer cents for this subscription.</summary>
    public long? PriceInCents { get; init; }

    /// <summary>Recurring price as a decimal amount.</summary>
    public decimal? Price => PriceInCents.HasValue ? PriceInCents.Value / 100m : null;

    /// <summary>When the next scheduled charge will occur (end of the current period, else next assessment).</summary>
    public DateTimeOffset? NextBillingDate { get; init; }

    public DateTimeOffset? CurrentPeriodEndsAt { get; init; }

    public DateTimeOffset? CreatedAt { get; init; }
}
