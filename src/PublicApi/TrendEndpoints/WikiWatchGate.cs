using System;
using System.Threading;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.TrendEndpoints;

/// <summary>
/// Caps how many watches run at once in this process — each one holds a connection to Wikimedia for up to a minute.
/// </summary>
public class WikiWatchGate
{
    private readonly SemaphoreSlim _slots;

    public WikiWatchGate(IOptions<WikiTrendsOptions> options)
    {
        _slots = new SemaphoreSlim(options.Value.MaxConcurrentWatches);
    }

    /// <summary>
    /// Returns a lease to dispose when the watch ends, or null when every slot is taken.
    /// </summary>
    public IDisposable? TryEnter() => _slots.Wait(0) ? new Lease(_slots) : null;

    private sealed class Lease : IDisposable
    {
        private SemaphoreSlim? _slots;

        public Lease(SemaphoreSlim slots) => _slots = slots;

        public void Dispose() => Interlocked.Exchange(ref _slots, null)?.Release();
    }
}
