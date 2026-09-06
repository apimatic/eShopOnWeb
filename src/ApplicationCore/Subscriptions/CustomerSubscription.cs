using System;

namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// One shopper's enrollment in a plan, as the billing system of record currently reports it.
/// </summary>
/// <param name="State">The provider's own state value (for example <c>active</c>), passed through verbatim
/// rather than mapped onto an eShopOnWeb enum — the provider may introduce states this build has never
/// heard of, and silently collapsing one of those into a familiar value would be worse than showing it.</param>
/// <param name="NextBillingDate">When the subscription is next assessed. Null while the provider has not
/// scheduled one (for example a subscription that is already canceled).</param>
public sealed record CustomerSubscription(
    int Id,
    string? PlanHandle,
    string PlanName,
    long? PriceInCents,
    string? Currency,
    string State,
    DateTimeOffset? NextBillingDate,
    DateTimeOffset? CurrentPeriodStartedAt,
    DateTimeOffset? CurrentPeriodEndsAt,
    DateTimeOffset? ActivatedAt,
    int? CustomerId)
{
    /// <summary>The recurring price in major currency units, when the provider reported one.</summary>
    public decimal? Price => PriceInCents is null ? null : PriceInCents.Value / 100m;
}
