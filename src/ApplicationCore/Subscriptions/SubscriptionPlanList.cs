using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// The result of listing subscription plans. <see cref="Truncated"/> is set when paging hit its
/// safety cap, so a caller can tell a partial list from a complete one.
/// </summary>
public record SubscriptionPlanList
{
    public required IReadOnlyList<SubscriptionPlan> Plans { get; init; }

    /// <summary>True when the plan list was cut short by the paging safety cap (partial result).</summary>
    public bool Truncated { get; init; }
}
