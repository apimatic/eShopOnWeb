using System;

namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// A subscription held by a shopper, as reported by the billing provider.
/// </summary>
public class CustomerSubscription
{
    public int Id { get; set; }

    /// <summary>Stable handle of the plan this subscription is on.</summary>
    public string? PlanHandle { get; set; }

    public string? PlanName { get; set; }

    /// <summary>Provider subscription state, e.g. <c>active</c>, <c>trialing</c>, <c>canceled</c>.</summary>
    public string? State { get; set; }

    /// <summary>Recurring price of the plan on this subscription, in minor units.</summary>
    public long PriceInCents { get; set; }

    public decimal Price => PriceInCents / 100m;

    public string? Currency { get; set; }

    /// <summary>How the provider collects this subscription's balance, e.g. <c>remittance</c>.</summary>
    public string? PaymentCollectionMethod { get; set; }

    public DateTimeOffset? CurrentPeriodStartedAt { get; set; }

    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }

    /// <summary>When the provider will next bill this subscription.</summary>
    public DateTimeOffset? NextBillingAt { get; set; }

    public long TotalRevenueInCents { get; set; }

    public decimal TotalRevenue => TotalRevenueInCents / 100m;

    public DateTimeOffset? CreatedAt { get; set; }

    public DateTimeOffset? CanceledAt { get; set; }
}
