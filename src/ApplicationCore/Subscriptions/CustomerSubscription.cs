using System;

namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// A shopper's subscription as reported by the billing system of record.
/// </summary>
public class CustomerSubscription
{
    /// <summary>Identifier of the subscription in the billing provider.</summary>
    public long Id { get; init; }

    /// <summary>Provider lifecycle state, e.g. "active", "trialing", "past_due", "canceled".</summary>
    public string State { get; init; } = string.Empty;

    /// <summary>True when the subscription still entitles the shopper to the product.</summary>
    public bool IsLive { get; init; }

    public string? PlanHandle { get; init; }

    public string? PlanName { get; init; }

    /// <summary>Recurring price captured on the subscription, in the smallest currency unit.</summary>
    public long PriceInCents { get; init; }

    public string? Currency { get; init; }

    public int Interval { get; init; }

    public string? IntervalUnit { get; init; }

    public DateTimeOffset? CurrentPeriodStartedAt { get; init; }

    public DateTimeOffset? CurrentPeriodEndsAt { get; init; }

    /// <summary>When the provider will next attempt to bill this subscription.</summary>
    public DateTimeOffset? NextBillingAt { get; init; }

    public DateTimeOffset? ActivatedAt { get; init; }

    public DateTimeOffset? CanceledAt { get; init; }

    public DateTimeOffset? TrialEndsAt { get; init; }

    /// <summary>How the provider collects payment, e.g. "automatic" or "remittance".</summary>
    public string? PaymentCollectionMethod { get; init; }

    /// <summary>Outstanding balance on the subscription, in the smallest currency unit.</summary>
    public long BalanceInCents { get; init; }

    public long BillingCustomerId { get; init; }

    public string? BillingCustomerReference { get; init; }
}
