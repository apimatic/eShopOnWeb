using System;
using System.Threading;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

/// <summary>Time and size limits for every Maxio call this application makes.</summary>
public static class MaxioBillingLimits
{
    /// <summary>SDK per-attempt timeout (retried for GETs only).</summary>
    public static readonly TimeSpan AttemptTimeout = TimeSpan.FromSeconds(8);

    /// <summary>HttpClient per-attempt backstop; never retried.</summary>
    public static readonly TimeSpan HttpClientTimeout = TimeSpan.FromSeconds(10);

    /// <summary>SDK retries for idempotent verbs (GET); POST is never resent.</summary>
    public const int MaxRetries = 2;

    /// <summary>Whole-request budget for read endpoints (plans, my subscriptions).</summary>
    public static readonly TimeSpan ReadBudget = TimeSpan.FromSeconds(15);

    /// <summary>Whole-request budget for subscribe: validate + ensure customer + pre-check + create.</summary>
    public static readonly TimeSpan WriteBudget = TimeSpan.FromSeconds(30);

    /// <summary>Budget for the re-read that settles a write whose outcome is unknown; independent of the caller's token.</summary>
    public static readonly TimeSpan ReconcileBudget = TimeSpan.FromSeconds(10);

    /// <summary>A Pending claim older than this belongs to a request that died; it may be taken over and settled.</summary>
    public static readonly TimeSpan StaleClaimAfter = TimeSpan.FromMinutes(2);

    /// <summary>Products requested per page (the API maximum).</summary>
    public const int PlanPageSize = 200;

    /// <summary>Hard cap on product pages read; past it the catalog is reported as truncated.</summary>
    public const int MaxPlanPages = 5;

    /// <summary>Links the caller's token to a deadline; dispose the result when the call completes.</summary>
    public static CancellationTokenSource StartBudget(TimeSpan budget, CancellationToken callerToken)
    {
        var cts = CancellationTokenSource.CreateLinkedTokenSource(callerToken);
        cts.CancelAfter(budget);
        return cts;
    }
}
