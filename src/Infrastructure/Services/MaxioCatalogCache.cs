using System;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.Infrastructure.Services;

/// <summary>
/// Caches the handle-to-id resolution of the Maxio catalog so the lookup happens once per lifetime
/// window rather than on every request. Registered as a singleton.
/// </summary>
/// <remarks>
/// The entry expires after <see cref="Lifetime"/>, so a provider-side change — a re-seed with new
/// ids, a new plan, a price change — is picked up without restarting the host, while a burst of
/// requests still costs a single round-trip. Expiry is time-based on purpose: nothing a caller can
/// supply forces a re-resolution, so an unknown plan handle cannot be used to amplify load. Only
/// successful resolutions are cached; a failure is retried on the next call.
/// </remarks>
public sealed class MaxioCatalogCache : IDisposable
{
    /// <summary>How long a resolved catalog is served before it is looked up again.</summary>
    public static readonly TimeSpan DefaultLifetime = TimeSpan.FromMinutes(5);

    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly TimeProvider _timeProvider;
    private Entry? _entry;
    private bool _disposed;

    public MaxioCatalogCache() : this(TimeProvider.System, DefaultLifetime)
    {
    }

    public MaxioCatalogCache(TimeProvider timeProvider, TimeSpan lifetime)
    {
        if (lifetime <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(lifetime), lifetime, "The catalog lifetime must be positive.");
        }

        _timeProvider = timeProvider;
        Lifetime = lifetime;
    }

    public TimeSpan Lifetime { get; }

    /// <summary>
    /// Returns the cached catalog, resolving it through <paramref name="factory"/> on first use and
    /// again once the cached entry has aged past <see cref="Lifetime"/>.
    /// </summary>
    public async Task<MaxioCatalog> GetAsync(Func<CancellationToken, Task<MaxioCatalog>> factory, CancellationToken cancellationToken)
    {
        var cached = Volatile.Read(ref _entry);
        if (cached is not null && !IsExpired(cached))
        {
            return cached.Catalog;
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            // Another caller may have refreshed it while this one waited for the gate.
            cached = Volatile.Read(ref _entry);
            if (cached is not null && !IsExpired(cached))
            {
                return cached.Catalog;
            }

            var resolved = await factory(cancellationToken);
            Volatile.Write(ref _entry, new Entry(resolved, _timeProvider.GetUtcNow()));

            return resolved;
        }
        finally
        {
            _gate.Release();
        }
    }

    private bool IsExpired(Entry entry) => _timeProvider.GetUtcNow() - entry.ResolvedAt >= Lifetime;

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _gate.Dispose();
    }

    private sealed record Entry(MaxioCatalog Catalog, DateTimeOffset ResolvedAt);
}
