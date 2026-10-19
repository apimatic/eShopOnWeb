using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

public record SubscriptionPlan(
    string Handle,
    string Name,
    string? Description,
    decimal Price,
    int? Interval,
    string? IntervalUnit);

/// <param name="Truncated">
/// True when the provider listing was cut short by the page cap, so <paramref name="Plans"/> may be incomplete.
/// </param>
public record SubscriptionPlanList(IReadOnlyList<SubscriptionPlan> Plans, bool Truncated);
