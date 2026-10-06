using System;
using System.Threading;

namespace Microsoft.eShopWeb.Infrastructure.SquareIntegration;

/// <summary>
/// Time budgets for Square work. The per-attempt bounds live on the SDK client and its HttpClient; the budgets
/// below bound a whole API request, whatever number of Square calls it makes.
/// </summary>
public static class SquareTimeouts
{
    /// <summary>One SDK attempt (<c>RetryOptions.Timeout</c>).</summary>
    public static readonly TimeSpan PerAttempt = TimeSpan.FromSeconds(10);

    /// <summary>HttpClient backstop; ends a hung attempt even when it is not retried.</summary>
    public static readonly TimeSpan HttpClientTimeout = TimeSpan.FromSeconds(15);

    /// <summary>Connect / callback / connection status / order / photo requests.</summary>
    public static readonly TimeSpan Request = TimeSpan.FromSeconds(45);

    /// <summary>A full catalog sync (one write per changed item, serialized per merchant).</summary>
    public static readonly TimeSpan CatalogSync = TimeSpan.FromSeconds(180);

    /// <summary>Creates the single deadline every Square call inside one API request shares.</summary>
    public static CancellationTokenSource Deadline(CancellationToken requestAborted, TimeSpan budget)
    {
        var cts = CancellationTokenSource.CreateLinkedTokenSource(requestAborted);
        cts.CancelAfter(budget);
        return cts;
    }
}
