using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// A shopper's subscription, as held by the billing system of record.
/// </summary>
public class SubscriptionDto
{
    /// <summary>Subscription id in the billing system.</summary>
    public long Id { get; set; }

    /// <summary>Billing state, e.g. <c>active</c>, <c>trialing</c>, <c>past_due</c>, <c>canceled</c>.</summary>
    public string State { get; set; } = string.Empty;

    /// <summary>True while the subscription still entitles the shopper to the plan.</summary>
    public bool IsActive { get; set; }

    public string? PlanHandle { get; set; }

    public string? PlanName { get; set; }

    /// <summary>The recurring amount actually billed, in the smallest currency unit.</summary>
    public long PriceInCents { get; set; }

    public decimal Price { get; set; }

    public string? Currency { get; set; }

    public int? Interval { get; set; }

    public string? IntervalUnit { get; set; }

    /// <summary>Human-readable price, e.g. "$299.00 / month".</summary>
    public string DisplayPrice { get; set; } = string.Empty;

    public DateTimeOffset? CurrentPeriodStartedAt { get; set; }

    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }

    /// <summary>When the billing system will next bill this subscription.</summary>
    public DateTimeOffset? NextBillingAt { get; set; }

    public DateTimeOffset? ActivatedAt { get; set; }

    public DateTimeOffset? CanceledAt { get; set; }

    /// <summary>Outstanding balance in the smallest currency unit.</summary>
    public long BalanceInCents { get; set; }

    /// <summary>Customer id in the billing system.</summary>
    public long CustomerId { get; set; }

    /// <summary>The billing-system customer reference this app derives from the signed-in user.</summary>
    public string? CustomerReference { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}
