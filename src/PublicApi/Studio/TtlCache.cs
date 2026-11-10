using System;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.PublicApi.Studio;

/// <summary>
/// Holds one value that is reloaded once it is older than its time to live, so that a long-running
/// process never serves a value that was loaded weeks ago.
/// </summary>
public sealed class TtlCache<T> where T : class
{
    private readonly TimeSpan _ttl;
    private readonly TimeProvider _clock;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private (T Value, DateTimeOffset LoadedAt)? _entry;

    public TtlCache(TimeSpan ttl, TimeProvider clock)
    {
        _ttl = ttl;
        _clock = clock;
    }

    public async Task<T> GetAsync(Func<CancellationToken, Task<T>> load, CancellationToken ct)
    {
        var entry = _entry;
        if (entry is { } e && _clock.GetUtcNow() - e.LoadedAt < _ttl)
        {
            return e.Value;
        }

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            entry = _entry;
            if (entry is { } again && _clock.GetUtcNow() - again.LoadedAt < _ttl)
            {
                return again.Value;
            }

            var value = await load(ct).ConfigureAwait(false);
            _entry = (value, _clock.GetUtcNow());
            return value;
        }
        finally
        {
            _gate.Release();
        }
    }
}
