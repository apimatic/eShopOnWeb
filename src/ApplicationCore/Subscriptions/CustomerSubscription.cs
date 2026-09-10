using System;

namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// A shopper's enrollment in a plan, projected from the billing system of record.
/// </summary>
public class CustomerSubscription
{
    /// <summary>The billing system's subscription id.</summary>
    public long Id { get; init; }

    /// <summary>Lifecycle state as reported by the billing system (e.g. "active").</summary>
    public string State { get; init; } = string.Empty;

    public string PlanHandle { get; init; } = string.Empty;

    public string PlanName { get; init; } = string.Empty;

    public int PriceInCents { get; init; }

    public int Interval { get; init; }

    public string IntervalUnit { get; init; } = string.Empty;

    /// <summary>How the balance is collected (e.g. "remittance" for invoice-based billing).</summary>
    public string? PaymentCollectionMethod { get; init; }

    /// <summary>When the current billing period ends.</summary>
    public DateTimeOffset? CurrentPeriodEndsAt { get; init; }

    /// <summary>When the subscription will next be assessed / billed.</summary>
    public DateTimeOffset? NextBillingAt { get; init; }

    public DateTimeOffset? CreatedAt { get; init; }
}
