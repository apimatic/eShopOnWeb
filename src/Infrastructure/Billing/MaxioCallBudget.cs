using System;
using System.Threading;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

/// <summary>
/// The one place an inbound call's total Maxio budget lives. <see cref="Work"/> bounds ordinary calls and follows the
/// caller's own cancellation; <see cref="Settlement"/> outlives both (but not the total budget) so an unknown write
/// outcome is still re-read when the work budget ran out or the caller went away.
/// </summary>
public sealed class MaxioCallBudget : IDisposable
{
    private readonly CancellationTokenSource _total;
    private readonly CancellationTokenSource _work;
    private readonly CancellationToken _callerToken;

    public MaxioCallBudget(MaxioBillingLimits limits, CancellationToken callerToken)
    {
        _callerToken = callerToken;
        _total = new CancellationTokenSource(limits.TotalBudget);
        _work = CancellationTokenSource.CreateLinkedTokenSource(callerToken, _total.Token);
        var workBudget = limits.TotalBudget - limits.SettlementReserve;
        _work.CancelAfter(workBudget > TimeSpan.Zero ? workBudget : limits.TotalBudget);
    }

    public CancellationToken Work => _work.Token;

    public CancellationToken Settlement => _total.Token;

    /// <summary>True when the caller itself cancelled (disconnected), as opposed to the budget expiring.</summary>
    public bool CallerCancelled => _callerToken.IsCancellationRequested;

    public void Dispose()
    {
        _work.Dispose();
        _total.Dispose();
    }
}
