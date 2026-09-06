using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// A subscription held by the authenticated shopper, as reported by the billing system of record.
/// </summary>
public class SubscriptionDto
{
    /// <summary>The subscription id in the billing system.</summary>
    public long Id { get; set; }

    /// <summary>The reference eShopOnWeb assigned to the subscription.</summary>
    public string? Reference { get; set; }

    /// <summary>The handle of the subscribed plan.</summary>
    public string PlanHandle { get; set; } = string.Empty;

    public string PlanName { get; set; } = string.Empty;

    /// <summary>The subscription state, e.g. <c>active</c>, <c>trialing</c> or <c>canceled</c>.</summary>
    public string State { get; set; } = string.Empty;

    /// <summary>True while the subscription entitles the shopper to the plan.</summary>
    public bool IsLive { get; set; }

    /// <summary>Recurring price in major currency units, e.g. 299.00.</summary>
    public decimal Price { get; set; }

    /// <summary>Recurring price in the smallest currency unit, e.g. 29900.</summary>
    public long PriceInCents { get; set; }

    public string? Currency { get; set; }

    public DateTimeOffset? CurrentPeriodStartedAt { get; set; }

    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }

    /// <summary>When the next charge will be attempted.</summary>
    public DateTimeOffset? NextBillingAt { get; set; }

    public DateTimeOffset? ActivatedAt { get; set; }

    public DateTimeOffset? CanceledAt { get; set; }

    public DateTimeOffset? CreatedAt { get; set; }

    /// <summary>The shopper's customer id in the billing system.</summary>
    public long CustomerId { get; set; }
}
