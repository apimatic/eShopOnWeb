using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// A recurring plan a customer can subscribe to, as exposed by the billing provider.
/// </summary>
/// <param name="Id">The provider-assigned numeric identifier. Not stable across a sandbox re-seed.</param>
/// <param name="Handle">The stable, human-authored identifier. This is what configuration refers to.</param>
/// <param name="Name">Display name.</param>
/// <param name="Description">Display description, if the provider supplies one.</param>
/// <param name="PriceInCents">Recurring price, in cents. The provider's canonical money unit.</param>
/// <param name="Interval">Number of <paramref name="IntervalUnit"/>s in one billing period.</param>
/// <param name="IntervalUnit">The billing period unit, e.g. "month".</param>
/// <param name="RequiresPaymentMethod">Whether enrolling demands a stored payment method.</param>
/// <param name="ArchivedAt">When the plan was archived, or <see langword="null"/> if it is live.</param>
public record BillingPlan(
    int Id,
    string Handle,
    string Name,
    string? Description,
    long PriceInCents,
    int Interval,
    string IntervalUnit,
    bool RequiresPaymentMethod,
    DateTimeOffset? ArchivedAt)
{
    /// <summary>The recurring price expressed in the site's currency unit (dollars).</summary>
    public decimal Price => PriceInCents / 100m;

    /// <summary>Whether the plan is archived and therefore not a valid subscribe target.</summary>
    public bool IsArchived => ArchivedAt.HasValue;

    /// <summary>A short human-readable billing cadence, e.g. "month" or "3 months".</summary>
    public string BillingPeriod => Interval == 1 ? IntervalUnit : $"{Interval} {IntervalUnit}s";
}
