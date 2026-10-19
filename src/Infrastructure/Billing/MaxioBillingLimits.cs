using System;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

/// <summary>
/// Time and size bounds for the Maxio integration. Defaults are the production values; tests shrink them.
/// </summary>
public sealed record MaxioBillingLimits
{
    /// <summary>Total budget for one inbound endpoint call, across every Maxio call it makes (including settlement).</summary>
    public TimeSpan TotalBudget { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// The tail of <see cref="TotalBudget"/> kept for settling an unknown write outcome; ordinary calls are
    /// cancelled at <c>TotalBudget - SettlementReserve</c>.
    /// </summary>
    public TimeSpan SettlementReserve { get; init; } = TimeSpan.FromSeconds(8);

    /// <summary>SDK per-attempt timeout (<c>RetryOptions.Timeout</c>).</summary>
    public TimeSpan PerAttemptTimeout { get; init; } = TimeSpan.FromSeconds(8);

    /// <summary>SDK retry count for retryable verbs (GET). POST is never resent by the SDK.</summary>
    public int MaxRetries { get; init; } = 2;

    /// <summary><c>HttpClient.Timeout</c> backstop; bounds one attempt and is not retried.</summary>
    public TimeSpan HttpClientTimeout { get; init; } = TimeSpan.FromSeconds(10);

    /// <summary>Page size for the plan listing (the provider maximum).</summary>
    public int PlansPerPage { get; init; } = 200;

    /// <summary>Page cap for the plan listing; hitting it with a full last page marks the catalog truncated.</summary>
    public int MaxPlanPages { get; init; } = 5;

    /// <summary>A claim still pending after this long belongs to a request that died and is reconciled.</summary>
    public TimeSpan StalePendingAfter { get; init; } = TimeSpan.FromMinutes(2);
}
