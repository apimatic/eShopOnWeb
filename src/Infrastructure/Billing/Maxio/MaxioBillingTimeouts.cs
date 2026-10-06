using System;

namespace Microsoft.eShopWeb.Infrastructure.Billing.Maxio;

/// <summary>
/// Time budgets for one API request that talks to Maxio. <see cref="RequestBudget"/> +
/// <see cref="ReconciliationBudget"/> is the longest a caller can wait on Maxio (25 s by default, inside the
/// 30 s ceiling); the per-attempt SDK timeout only bounds a single HTTP attempt.
/// </summary>
public record MaxioBillingTimeouts
{
    /// <summary>Deadline shared by every Maxio call one API request makes.</summary>
    public TimeSpan RequestBudget { get; init; } = TimeSpan.FromSeconds(20);

    /// <summary>Extra time reserved to look up a subscription whose create call ended without a usable answer.</summary>
    public TimeSpan ReconciliationBudget { get; init; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// A pending claim older than this cannot belong to a request still in flight, so it is settled against Maxio
    /// (and released when Maxio holds nothing for it) on the shopper's next subscribe attempt.
    /// </summary>
    public TimeSpan StaleClaimAge { get; init; } = TimeSpan.FromMinutes(2);

    /// <summary>How long the plan list is cached.</summary>
    public TimeSpan PlanCacheDuration { get; init; } = TimeSpan.FromSeconds(60);
}
