using System;
using Ardalis.GuardClauses;

namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// A shopper's subscription as the billing system - the system of record - currently reports it.
/// </summary>
public class SubscriptionSummary
{
    public SubscriptionSummary(string id, string state)
    {
        Id = Guard.Against.NullOrWhiteSpace(id, nameof(id));
        State = Guard.Against.NullOrWhiteSpace(state, nameof(state));
    }

    /// <summary>The billing system's subscription id.</summary>
    public string Id { get; }

    /// <summary>The billing system's subscription state, e.g. "active" or "past_due".</summary>
    public string State { get; }

    public string? CustomerId { get; init; }

    public string? PlanHandle { get; init; }

    public string? PlanName { get; init; }

    public long PriceInCents { get; init; }

    public decimal Price => PriceInCents / 100m;

    public string? Currency { get; init; }

    public int Interval { get; init; }

    public string? IntervalUnit { get; init; }

    /// <summary>How the billing system collects payment: "automatic", "remittance", "invoice" or "prepaid".</summary>
    public string? PaymentCollectionMethod { get; init; }

    /// <summary>Outstanding amount on the subscription, in cents.</summary>
    public long BalanceInCents { get; init; }

    public decimal Balance => BalanceInCents / 100m;

    /// <summary>When the next renewal charge is scheduled. Null while the subscription is not billing.</summary>
    public DateTimeOffset? NextBillingAt { get; init; }

    public DateTimeOffset? CurrentPeriodStartedAt { get; init; }

    public DateTimeOffset? CurrentPeriodEndsAt { get; init; }

    public DateTimeOffset? ActivatedAt { get; init; }

    public DateTimeOffset? CanceledAt { get; init; }

    public DateTimeOffset? CreatedAt { get; init; }

    /// <summary>True while this subscription still ties the shopper to the plan.</summary>
    public bool IsCurrent => SubscriptionStates.IsCurrent(State);
}
