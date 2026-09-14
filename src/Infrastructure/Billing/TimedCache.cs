using System;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

/// <summary>
/// A single value refreshed from the billing system at most once per TTL, with concurrent callers collapsed
/// onto one refresh.
/// </summary>
/// <remarks>
/// Used for facts that are stable but not permanent — the numeric id behind a product-family handle, and the
/// site currency. Maxio reassigns numeric ids when a catalog is re-seeded, which is exactly why the value is
/// cached with an expiry and can be dropped on demand rather than resolved once for the process lifetime.
/// </remarks>
internal sealed class TimedCache<T> where T : class
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly TimeSpan _timeToLive;

    private T? _value;
    private DateTimeOffset _expiresAt;

    public TimedCache(TimeSpan timeToLive) => _timeToLive = timeToLive;

    public async Task<T> GetAsync(Func<CancellationToken, Task<T>> factory, CancellationToken cancellationToken)
    {
        var cached = Volatile.Read(ref _value);

        if (cached is not null && DateTimeOffset.UtcNow < _expiresAt)
        {
            return cached;
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            cached = _value;

            if (cached is not null && DateTimeOffset.UtcNow < _expiresAt)
            {
                return cached;
            }

            var fresh = await factory(cancellationToken).ConfigureAwait(false);
            _expiresAt = DateTimeOffset.UtcNow.Add(_timeToLive);
            Volatile.Write(ref _value, fresh);
            return fresh;
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Invalidate()
    {
        Volatile.Write(ref _value, null);
        _expiresAt = DateTimeOffset.MinValue;
    }
}
