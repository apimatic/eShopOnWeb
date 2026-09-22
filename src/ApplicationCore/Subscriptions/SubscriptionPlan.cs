using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>A subscribable plan (a Maxio product within the configured product family).</summary>
public record SubscriptionPlan(
    string Handle,
    string Name,
    string? Description,
    long PriceInCents,
    int? Interval,
    string? IntervalUnit,
    bool PaymentMethodRequired);

/// <summary>
/// The available plans. <see cref="Truncated"/> is true when the page cap was hit before the provider
/// signalled the end of the list — so the caller learns the answer is partial from the result, not a log.
/// </summary>
public record SubscriptionPlanList(IReadOnlyList<SubscriptionPlan> Plans, bool Truncated);
