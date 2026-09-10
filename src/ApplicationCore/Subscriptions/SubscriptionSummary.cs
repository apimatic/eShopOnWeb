using System;

namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// A shopper-facing snapshot of a Maxio subscription: which plan, at what price, in what
/// state, and when it bills next. Maxio remains the system of record; this is a read model.
/// </summary>
public class SubscriptionSummary
{
    public SubscriptionSummary(
        int id,
        string state,
        string planHandle,
        string planName,
        long priceInCents,
        int interval,
        string intervalUnit,
        string paymentCollectionMethod,
        DateTimeOffset? currentPeriodStartedAt,
        DateTimeOffset? nextBillingAt)
    {
        Id = id;
        State = state;
        PlanHandle = planHandle;
        PlanName = planName;
        PriceInCents = priceInCents;
        Interval = interval;
        IntervalUnit = intervalUnit;
        PaymentCollectionMethod = paymentCollectionMethod;
        CurrentPeriodStartedAt = currentPeriodStartedAt;
        NextBillingAt = nextBillingAt;
    }

    /// <summary>Maxio subscription id (the system-of-record identifier).</summary>
    public int Id { get; }

    /// <summary>Maxio lifecycle state as reported by the API, e.g. "active", "past_due".</summary>
    public string State { get; }

    public string PlanHandle { get; }
    public string PlanName { get; }
    public long PriceInCents { get; }
    public int Interval { get; }
    public string IntervalUnit { get; }
    public string PaymentCollectionMethod { get; }
    public DateTimeOffset? CurrentPeriodStartedAt { get; }

    /// <summary>Next assessment / billing date reported by Maxio.</summary>
    public DateTimeOffset? NextBillingAt { get; }

    public string PriceDisplay =>
        $"${PriceInCents / 100m:0.00}/{(Interval == 1 ? IntervalUnit : $"{Interval} {IntervalUnit}s")}";
}
