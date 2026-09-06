using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// A subscription of the authenticated shopper as exposed by the public API.
/// </summary>
public class SubscriptionDto
{
    /// <summary>Identifier of the subscription in the billing system of record.</summary>
    public long Id { get; set; }

    /// <summary>The reference eShopOnWeb stamped on the subscription.</summary>
    public string? Reference { get; set; }

    /// <summary>Lifecycle state, e.g. "active", "trialing", "canceled".</summary>
    public string State { get; set; } = string.Empty;

    /// <summary>True while the subscription still entitles the shopper to the service.</summary>
    public bool IsActive { get; set; }

    /// <summary>Handle of the subscribed plan.</summary>
    public string? PlanHandle { get; set; }

    /// <summary>Display name of the subscribed plan.</summary>
    public string? PlanName { get; set; }

    /// <summary>Recurring price in major units.</summary>
    public decimal Price { get; set; }

    /// <summary>Recurring price in minor units.</summary>
    public long PriceInCents { get; set; }

    /// <summary>ISO currency code, when the provider reports one.</summary>
    public string? Currency { get; set; }

    /// <summary>Number of <see cref="IntervalUnit"/>s per billing period.</summary>
    public int Interval { get; set; }

    /// <summary>Billing period unit, "day" or "month".</summary>
    public string? IntervalUnit { get; set; }

    /// <summary>Human-readable billing period, e.g. "every month".</summary>
    public string? BillingPeriod { get; set; }

    /// <summary>When payment will next be attempted.</summary>
    public DateTimeOffset? NextBillingAt { get; set; }

    /// <summary>Start of the current billing period.</summary>
    public DateTimeOffset? CurrentPeriodStartedAt { get; set; }

    /// <summary>End of the current billing period.</summary>
    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }

    /// <summary>When the subscription became active.</summary>
    public DateTimeOffset? ActivatedAt { get; set; }

    /// <summary>When the trial period ended, if there was one.</summary>
    public DateTimeOffset? TrialEndedAt { get; set; }

    /// <summary>When the subscription was created.</summary>
    public DateTimeOffset? CreatedAt { get; set; }

    /// <summary>When the subscription was canceled, if it was.</summary>
    public DateTimeOffset? CanceledAt { get; set; }

    /// <summary>Outstanding balance in minor units.</summary>
    public long BalanceInCents { get; set; }

    /// <summary>How payment is collected, e.g. "automatic".</summary>
    public string? PaymentCollectionMethod { get; set; }

    /// <summary>Identifier of the billing customer record that owns the subscription.</summary>
    public long CustomerId { get; set; }

    /// <summary>Reference eShopOnWeb stamped on the billing customer record.</summary>
    public string? CustomerReference { get; set; }
}
