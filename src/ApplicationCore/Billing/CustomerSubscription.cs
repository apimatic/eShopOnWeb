using System;

namespace Microsoft.eShopWeb.ApplicationCore.Billing;

/// <summary>
/// A customer's subscription as it exists in Maxio, projected to the fields
/// eShopOnWeb cares about (plan, price, state, and next-billing date).
/// </summary>
public record CustomerSubscription(
    long Id,
    string State,
    string? ProductHandle,
    string? ProductName,
    int PriceInCents,
    decimal Price,
    int Interval,
    string? IntervalUnit,
    DateTimeOffset? CurrentPeriodEndsAt,
    DateTimeOffset? NextAssessmentAt,
    DateTimeOffset? ActivatedAt,
    DateTimeOffset? CreatedAt,
    string? CustomerReference,
    int CustomerId)
{
    /// <summary>
    /// The next date the subscription will be billed. Prefers the current period end,
    /// falling back to the next assessment date.
    /// </summary>
    public DateTimeOffset? NextBillingDate => CurrentPeriodEndsAt ?? NextAssessmentAt;

    /// <summary>True when the subscription is live (not canceled or expired).</summary>
    public bool IsLive =>
        !string.Equals(State, "canceled", StringComparison.OrdinalIgnoreCase) &&
        !string.Equals(State, "expired", StringComparison.OrdinalIgnoreCase);
}
