using System;

namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// A subscription that belongs to a Maxio customer, projected into the fields eShopOnWeb needs to
/// confirm plan / price / state / next-billing-date back to the shopper.
/// </summary>
public class CustomerSubscription
{
    public long Id { get; init; }

    /// <summary>Maxio subscription state (e.g. <c>active</c>, <c>trialing</c>, <c>canceled</c>).</summary>
    public string State { get; init; } = string.Empty;

    public string ProductHandle { get; init; } = string.Empty;

    public string ProductName { get; init; } = string.Empty;

    public long ProductPriceInCents { get; init; }

    public decimal ProductPrice => ProductPriceInCents / 100m;

    public int ProductInterval { get; init; }

    public string ProductIntervalUnit { get; init; } = string.Empty;

    public DateTimeOffset? CurrentPeriodStartedAt { get; init; }

    public DateTimeOffset? CurrentPeriodEndsAt { get; init; }

    public DateTimeOffset? NextAssessmentAt { get; init; }

    public DateTimeOffset? CreatedAt { get; init; }

    /// <summary>
    /// The date the next regularly scheduled charge will occur. Tracks
    /// <see cref="CurrentPeriodEndsAt"/>, falling back to <see cref="NextAssessmentAt"/>.
    /// </summary>
    public DateTimeOffset? NextBillingAt => CurrentPeriodEndsAt ?? NextAssessmentAt;
}
