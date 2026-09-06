using System;

namespace Microsoft.eShopWeb.ApplicationCore.Billing;

/// <summary>
/// One subscription held by an eShopOnWeb shopper, projected from the billing provider.
/// </summary>
public class CustomerSubscription
{
    public CustomerSubscription(int id,
        string? reference,
        string planHandle,
        string planName,
        long priceInCents,
        string? currency,
        string state,
        bool isLive,
        DateTimeOffset? nextBillingDate,
        DateTimeOffset? currentPeriodStartedAt,
        DateTimeOffset? createdAt)
    {
        Id = id;
        Reference = reference;
        PlanHandle = planHandle;
        PlanName = planName;
        PriceInCents = priceInCents;
        Currency = currency;
        State = state;
        IsLive = isLive;
        NextBillingDate = nextBillingDate;
        CurrentPeriodStartedAt = currentPeriodStartedAt;
        CreatedAt = createdAt;
    }

    /// <summary>Provider-assigned subscription id.</summary>
    public int Id { get; }

    /// <summary>The deterministic reference this integration assigns when it enrolls a shopper.</summary>
    public string? Reference { get; }

    public string PlanHandle { get; }

    public string PlanName { get; }

    public long PriceInCents { get; }

    public decimal Price => PriceInCents / 100m;

    /// <summary>ISO currency code reported by the provider for this subscription, when it supplies one.</summary>
    public string? Currency { get; }

    /// <summary>Raw provider state, for example "active" or "canceled".</summary>
    public string State { get; }

    /// <summary>
    /// True when the subscription is not in a terminal state, i.e. it still occupies the
    /// shopper's "already subscribed to this plan" slot.
    /// </summary>
    public bool IsLive { get; }

    /// <summary>
    /// When the next regularly scheduled charge is expected. Null when the provider does not
    /// report one — never substitute a retry timestamp here.
    /// </summary>
    public DateTimeOffset? NextBillingDate { get; }

    public DateTimeOffset? CurrentPeriodStartedAt { get; }

    public DateTimeOffset? CreatedAt { get; }
}
