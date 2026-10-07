using System;

namespace Microsoft.eShopWeb.ApplicationCore.Payments;

/// <summary>
/// Time limits for one pay/refund request. The request budget is the total time all provider calls made by a
/// single API request may take, so the caller never waits longer than that for the provider.
/// </summary>
public sealed class PaymentProcessingOptions
{
    /// <summary>Total time all provider calls of one API request may take (kept under the 30 s the API promises).</summary>
    public TimeSpan RequestBudget { get; set; } = TimeSpan.FromSeconds(25);

    /// <summary>Another provider call (a settle re-send) is only started when at least this much budget is left.</summary>
    public TimeSpan MinimumCallWindow { get; set; } = TimeSpan.FromSeconds(11);

    /// <summary>A claim older than this cannot belong to a request that is still running; it is settled like an unknown outcome.</summary>
    public TimeSpan StaleClaimAfter { get; set; } = TimeSpan.FromMinutes(2);
}
