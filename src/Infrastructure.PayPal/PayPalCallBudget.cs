using System;
using System.Threading;

namespace Microsoft.eShopWeb.Infrastructure.PayPal;

/// <summary>
/// One deadline per API request (scoped), shared by every PayPal call that request makes — the
/// first call starts the clock. Per-attempt SDK timeouts do not bound a request; this does.
/// </summary>
public sealed class PayPalCallBudget : IDisposable
{
    private readonly PayPalResilienceSettings _settings;
    private readonly TimeProvider _clock;
    private CancellationTokenSource? _deadline;
    private DateTimeOffset _expiresAt;

    public PayPalCallBudget(PayPalResilienceSettings settings, TimeProvider clock)
    {
        _settings = settings;
        _clock = clock;
    }

    public bool IsExhausted => _deadline is not null && _clock.GetUtcNow() >= _expiresAt;

    public TimeSpan Remaining
    {
        get
        {
            EnsureStarted();
            var left = _expiresAt - _clock.GetUtcNow();
            return left > TimeSpan.Zero ? left : TimeSpan.Zero;
        }
    }

    /// <summary>
    /// A token that fires at the request deadline — or earlier by <paramref name="reserve"/>, so a write
    /// leaves time to settle its outcome. Dispose the returned source after the call.
    /// </summary>
    public CancellationTokenSource Link(CancellationToken callerToken, TimeSpan reserve)
    {
        EnsureStarted();
        var linked = CancellationTokenSource.CreateLinkedTokenSource(callerToken, _deadline!.Token);
        var available = Remaining - reserve;
        // Never hand out less than a sliver for the call itself; the overall deadline still applies.
        linked.CancelAfter(available > TimeSpan.FromSeconds(1) ? available : Remaining);
        return linked;
    }

    private void EnsureStarted()
    {
        if (_deadline is not null)
            return;
        _expiresAt = _clock.GetUtcNow() + _settings.RequestBudget;
        _deadline = new CancellationTokenSource(_settings.RequestBudget, _clock);
    }

    public void Dispose() => _deadline?.Dispose();
}
