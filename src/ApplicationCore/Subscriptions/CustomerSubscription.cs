using System;
using System.Globalization;

namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// A subscription a customer holds in Maxio, projected to an application-facing model.
/// </summary>
public class CustomerSubscription
{
    /// <summary>Maxio subscription id.</summary>
    public int Id { get; init; }

    /// <summary>Maxio customer id that owns the subscription.</summary>
    public int CustomerId { get; init; }

    /// <summary>Lifecycle state (e.g. "active", "trialing", "canceled").</summary>
    public string State { get; init; } = string.Empty;

    public string ProductHandle { get; init; } = string.Empty;

    public string ProductName { get; init; } = string.Empty;

    /// <summary>The recurring product price for this subscription, in integer cents.</summary>
    public long ProductPriceInCents { get; init; }

    /// <summary>
    /// End of the current billing period — i.e. when the next charge is scheduled.
    /// Surfaced to callers as the "next billing date".
    /// </summary>
    public DateTimeOffset? CurrentPeriodEndsAt { get; init; }

    /// <summary>When Maxio will next attempt to assess/charge. Usually tracks the period end.</summary>
    public DateTimeOffset? NextAssessmentAt { get; init; }

    public DateTimeOffset? ActivatedAt { get; init; }

    public DateTimeOffset CreatedAt { get; init; }

    /// <summary>Convenience: the next billing date, preferring the assessment time when present.</summary>
    public DateTimeOffset? NextBillingDate => NextAssessmentAt ?? CurrentPeriodEndsAt;

    public string FormattedPrice =>
        (ProductPriceInCents / 100m).ToString("C2", CultureInfo.GetCultureInfo("en-US"));
}
