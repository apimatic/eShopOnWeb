using System;

namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// A shopper's subscription as reported by Maxio (the billing system of record).
/// </summary>
public sealed class CustomerSubscription
{
    public CustomerSubscription(
        long id,
        string state,
        string planHandle,
        string planName,
        int priceInCents,
        string currency,
        DateTimeOffset? currentPeriodStartedAt,
        DateTimeOffset? currentPeriodEndsAt,
        DateTimeOffset? nextBillingAt,
        int customerId,
        string? customerReference)
    {
        Id = id;
        State = state;
        PlanHandle = planHandle;
        PlanName = planName;
        PriceInCents = priceInCents;
        Currency = currency;
        CurrentPeriodStartedAt = currentPeriodStartedAt;
        CurrentPeriodEndsAt = currentPeriodEndsAt;
        NextBillingAt = nextBillingAt;
        CustomerId = customerId;
        CustomerReference = customerReference;
    }

    /// <summary>Maxio subscription id.</summary>
    public long Id { get; }

    /// <summary>Maxio subscription state (e.g. active, trialing, past_due, canceled).</summary>
    public string State { get; }

    public string PlanHandle { get; }

    public string PlanName { get; }

    public int PriceInCents { get; }

    public string Currency { get; }

    public DateTimeOffset? CurrentPeriodStartedAt { get; }

    public DateTimeOffset? CurrentPeriodEndsAt { get; }

    /// <summary>When Maxio will next assess (bill) the subscription.</summary>
    public DateTimeOffset? NextBillingAt { get; }

    public int CustomerId { get; }

    public string? CustomerReference { get; }
}
