using System;

namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// A shopper's subscription as confirmed by Maxio: which plan, at what price, in what state, and
/// when it next bills. Money is normalised from Maxio's integer cents to a decimal amount.
/// </summary>
public sealed record CustomerSubscription
{
    public int? Id { get; init; }
    public string? PlanHandle { get; init; }
    public string? PlanName { get; init; }

    /// <summary>Recurring price in the plan's currency (normalised from Maxio's integer cents).</summary>
    public decimal Price { get; init; }

    /// <summary>Raw price in cents as reported by Maxio.</summary>
    public long? PriceInCents { get; init; }

    /// <summary>Subscription state wire value as reported by Maxio (e.g. "active", "pending").</summary>
    public string? State { get; init; }

    /// <summary>Next assessment / billing date reported by Maxio.</summary>
    public DateTimeOffset? NextBillingDate { get; init; }

    public DateTimeOffset? CurrentPeriodEndsAt { get; init; }
}
