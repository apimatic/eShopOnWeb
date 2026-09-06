using System;

namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// A shopper's subscription as it currently stands in the billing provider.
/// </summary>
public class CustomerSubscription
{
    public CustomerSubscription(
        int id,
        string state,
        bool isLive,
        string planHandle,
        string planName,
        long priceInCents,
        string? currency,
        int interval,
        string intervalUnit,
        DateTimeOffset? nextBillingDate,
        DateTimeOffset? currentPeriodEndsAt,
        DateTimeOffset? createdAt,
        int customerId,
        string? customerReference)
    {
        Id = id;
        State = state;
        IsLive = isLive;
        PlanHandle = planHandle;
        PlanName = planName;
        PriceInCents = priceInCents;
        Currency = currency;
        Interval = interval;
        IntervalUnit = intervalUnit;
        NextBillingDate = nextBillingDate;
        CurrentPeriodEndsAt = currentPeriodEndsAt;
        CreatedAt = createdAt;
        CustomerId = customerId;
        CustomerReference = customerReference;
    }

    public int Id { get; }

    /// <summary>The provider wire value for the subscription state, e.g. active.</summary>
    public string State { get; }

    /// <summary>
    /// True while the subscription still exists as far as billing is concerned - i.e. it has not
    /// reached a terminal state. A live subscription is one that must not be duplicated.
    /// </summary>
    public bool IsLive { get; }

    public string PlanHandle { get; }

    public string PlanName { get; }

    public long PriceInCents { get; }

    public decimal Price => PriceInCents / 100m;

    public string? Currency { get; }

    public int Interval { get; }

    public string IntervalUnit { get; }

    /// <summary>
    /// When the next assessment falls due. Falls back to the current period end when the provider
    /// reports no assessment date, and stays null when it reports neither.
    /// </summary>
    public DateTimeOffset? NextBillingDate { get; }

    public DateTimeOffset? CurrentPeriodEndsAt { get; }

    public DateTimeOffset? CreatedAt { get; }

    public int CustomerId { get; }

    public string? CustomerReference { get; }
}
