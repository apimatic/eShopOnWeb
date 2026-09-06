using System;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

/// <summary>
/// Caches a single asynchronously produced value for a fixed time, collapsing concurrent misses into
/// one call to the factory.
/// </summary>
/// <remarks>
/// The plan catalog and the site currency change rarely but are read on every request, so they are
/// cached rather than re-fetched. The single-flight behaviour matters more than the caching itself:
/// without it, a burst of traffic on a cold cache would fan out into one upstream call per request,
/// which is exactly when the billing system is least able to absorb them.
/// </remarks>
public sealed class AsyncTtlCache<T> where T : class
{
    private readonly TimeSpan _timeToLive;
    private readonly Func<DateTimeOffset> _clock;
    private readonly SemaphoreSlim _refreshGate = new(1, 1);

    private T? _value;
    private DateTimeOffset _expiresAt;

    public AsyncTtlCache(TimeSpan timeToLive, Func<DateTimeOffset>? clock = null)
    {
        _timeToLive = timeToLive;
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
    }

    /// <summary>Returns the cached value, producing it with <paramref name="factory"/> when absent or stale.</summary>
    public async Task<T> GetAsync(Func<CancellationToken, Task<T>> factory, CancellationToken cancellationToken = default)
    {
        if (TryReadFresh(out var cached))
        {
            return cached;
        }

        await _refreshGate.WaitAsync(cancellationToken);
        try
        {
            // Another caller may have refreshed while this one queued.
            if (TryReadFresh(out cached))
            {
                return cached;
            }

            var value = await factory(cancellationToken);
            Volatile.Write(ref _value, value);
            _expiresAt = _clock() + _timeToLive;
            return value;
        }
        finally
        {
            _refreshGate.Release();
        }
    }

    /// <summary>Drops the cached value so the next read goes upstream.</summary>
    public void Invalidate() => _expiresAt = DateTimeOffset.MinValue;

    private bool TryReadFresh(out T value)
    {
        var snapshot = Volatile.Read(ref _value);
        if (snapshot is not null && _clock() < _expiresAt)
        {
            value = snapshot;
            return true;
        }

        value = default!;
        return false;
    }
}
