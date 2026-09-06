using System;

namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// A recurring plan a shopper can subscribe to. Sourced from the billing system of record;
/// eShopOnWeb never owns plan definitions locally.
/// </summary>
public class SubscriptionPlan
{
    /// <summary>Stable, human-readable identifier of the plan. This — not the numeric id — is the key callers use.</summary>
    public string Handle { get; init; } = string.Empty;

    public string Name { get; init; } = string.Empty;

    public string? Description { get; init; }

    /// <summary>Recurring price expressed in the minor unit of <see cref="Currency"/>.</summary>
    public long PriceInCents { get; init; }

    /// <summary>Recurring price expressed in the major unit of <see cref="Currency"/>.</summary>
    public decimal Price => decimal.Divide(PriceInCents, 100m);

    /// <summary>ISO currency code of the billing site (e.g. "USD").</summary>
    public string? Currency { get; init; }

    /// <summary>Number of <see cref="IntervalUnit"/>s in one billing period (e.g. 1 with "month" = monthly).</summary>
    public int Interval { get; init; }

    /// <summary>"day" or "month".</summary>
    public string? IntervalUnit { get; init; }

    /// <summary>True when the plan cannot be subscribed to without a stored payment method.</summary>
    public bool RequiresPaymentMethod { get; init; }

    public int? TrialInterval { get; init; }

    public string? TrialIntervalUnit { get; init; }

    public long? TrialPriceInCents { get; init; }

    /// <summary>Handle of the product family (catalog) the plan belongs to.</summary>
    public string? ProductFamilyHandle { get; init; }

    public DateTimeOffset? UpdatedAt { get; init; }
}
