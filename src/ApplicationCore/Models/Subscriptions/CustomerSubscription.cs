using System;

namespace Microsoft.eShopWeb.ApplicationCore.Models.Subscriptions;

/// <summary>
/// A subscription held by an eShopOnWeb shopper, as reported by the billing system of record.
/// </summary>
public class CustomerSubscription
{
    /// <summary>Subscription id in the billing system.</summary>
    public int Id { get; set; }

    /// <summary>Reference supplied by eShopOnWeb when the subscription was created.</summary>
    public string? Reference { get; set; }

    /// <summary>Lifecycle state, e.g. <c>active</c>, <c>trialing</c>, <c>past_due</c>, <c>canceled</c>.</summary>
    public string State { get; set; } = string.Empty;

    /// <summary>True when the subscription is in a state that entitles the shopper to the service.</summary>
    public bool IsLive => SubscriptionStates.IsLive(State);

    public string PlanHandle { get; set; } = string.Empty;

    public string PlanName { get; set; } = string.Empty;

    /// <summary>Recurring amount for this subscription in cents (the price at the time of signup).</summary>
    public long PriceInCents { get; set; }

    public decimal Price => PriceInCents / 100m;

    public string Currency { get; set; } = string.Empty;

    public int Interval { get; set; }

    public string IntervalUnit { get; set; } = string.Empty;

    /// <summary>When the next renewal charge is scheduled. Null for subscriptions that no longer renew.</summary>
    public DateTimeOffset? NextBillingAt { get; set; }

    public DateTimeOffset? CurrentPeriodStartedAt { get; set; }

    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }

    public DateTimeOffset? ActivatedAt { get; set; }

    public DateTimeOffset? CanceledAt { get; set; }

    public DateTimeOffset? TrialEndedAt { get; set; }

    /// <summary>Outstanding balance in cents.</summary>
    public long BalanceInCents { get; set; }

    /// <summary>How the subscription is collected, e.g. <c>automatic</c> or <c>remittance</c>.</summary>
    public string? PaymentCollectionMethod { get; set; }

    /// <summary>Customer id in the billing system.</summary>
    public int CustomerId { get; set; }

    /// <summary>Reference eShopOnWeb uses to identify the shopper in the billing system.</summary>
    public string? CustomerReference { get; set; }

    public string? CustomerEmail { get; set; }
}
