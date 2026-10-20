using System;
using System.Threading;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>
/// The single home of the Maxio time budget. Every inbound API request gets ONE token, linked to the request's
/// own abort token, that bounds ALL the Maxio calls made for it; the per-attempt SDK timeout and the
/// HttpClient timeout only bound single attempts.
/// </summary>
public static class MaxioCallBudget
{
    /// <summary>Total Maxio time one inbound API request may spend.</summary>
    public static readonly TimeSpan PerRequest = TimeSpan.FromSeconds(30);

    /// <summary>Budget for settling an unknown write outcome; fresh, because the request budget may be spent.</summary>
    public static readonly TimeSpan Reconciliation = TimeSpan.FromSeconds(10);

    /// <summary>HttpClient.Timeout backstop: above the SDK per-attempt timeout so the SDK's own timeout fires first.</summary>
    public static readonly TimeSpan HttpClientTimeout = TimeSpan.FromSeconds(15);

    public static CancellationTokenSource ForRequest(CancellationToken requestAborted)
    {
        var cts = CancellationTokenSource.CreateLinkedTokenSource(requestAborted);
        cts.CancelAfter(PerRequest);
        return cts;
    }

    public static CancellationTokenSource ForReconciliation() => new(Reconciliation);
}
