using System;

namespace Microsoft.eShopWeb.PublicApi.Billing;

/// <summary>
/// Time and size bounds for Maxio traffic. Code-level defaults (not configuration keys); tests substitute smaller values.
/// </summary>
public sealed record MaxioCallPolicy
{
    /// <summary>Total budget for ONE SDK call, covering every SDK retry attempt and backoff.</summary>
    public TimeSpan CallBudget { get; init; } = TimeSpan.FromSeconds(8);

    /// <summary>Total budget for one API request (all the Maxio calls a subscribe makes).</summary>
    public TimeSpan RequestBudget { get; init; } = TimeSpan.FromSeconds(45);

    /// <summary>SDK per-attempt timeout (never above <see cref="CallBudget"/>).</summary>
    public TimeSpan AttemptTimeout { get; init; } = TimeSpan.FromSeconds(6);

    /// <summary>SDK retries for retryable verbs (GET). POST is never resent by the SDK.</summary>
    public int MaxRetries { get; init; } = 2;

    /// <summary>
    /// A Pending claim younger than this is treated as in flight (the second caller gets 409). An older one is
    /// stale: its outcome is re-read from Maxio and the claim is settled or released. Must exceed <see cref="RequestBudget"/>.
    /// </summary>
    public TimeSpan InFlightWindow { get; init; } = TimeSpan.FromSeconds(60);

    /// <summary>Page size for the plan catalogue walk (Maxio clamps per_page above 200 to 200).</summary>
    public int PlanPageSize { get; init; } = 200;

    /// <summary>Page cap for the plan catalogue walk; hitting it with a full last page marks the result truncated.</summary>
    public int MaxPlanPages { get; init; } = 5;
}
