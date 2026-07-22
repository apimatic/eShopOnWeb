using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Services;

/// <summary>
/// Caches the catalog identifiers the billing client has to resolve from handles.
/// </summary>
/// <remarks>
/// Handles are the durable identifiers; the numeric ids Maxio assigns are reassigned whenever the
/// catalog is re-created, so they are resolved at runtime rather than configured. Several operations
/// only accept numeric ids, and resolving them costs a round trip, so they are cached in-process for a
/// bounded period — long enough to keep request paths cheap, short enough that a re-seeded sandbox
/// heals without a restart.
/// </remarks>
public sealed class MaxioCatalogCache
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly TimeSpan _lifetime;
    private readonly Func<DateTimeOffset> _clock;

    private int? _productFamilyId;
    private DateTimeOffset _productFamilyIdExpiresAt;
    private MeteredComponent? _meteredComponent;
    private DateTimeOffset _meteredComponentExpiresAt;

    public MaxioCatalogCache(TimeSpan lifetime) : this(lifetime, () => DateTimeOffset.UtcNow)
    {
    }

    internal MaxioCatalogCache(TimeSpan lifetime, Func<DateTimeOffset> clock)
    {
        _lifetime = lifetime > TimeSpan.Zero ? lifetime : TimeSpan.Zero;
        _clock = clock;
    }

    public async Task<int> GetProductFamilyIdAsync(Func<CancellationToken, Task<int>> resolver,
        CancellationToken cancellationToken)
    {
        if (TryReadProductFamilyId(out var cached))
        {
            return cached;
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (TryReadProductFamilyId(out cached))
            {
                return cached;
            }

            var resolved = await resolver(cancellationToken);
            _productFamilyId = resolved;
            _productFamilyIdExpiresAt = _clock() + _lifetime;
            return resolved;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<MeteredComponent> GetMeteredComponentAsync(Func<CancellationToken, Task<MeteredComponent>> resolver,
        CancellationToken cancellationToken)
    {
        if (TryReadMeteredComponent(out var cached))
        {
            return cached;
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (TryReadMeteredComponent(out cached))
            {
                return cached;
            }

            var resolved = await resolver(cancellationToken);
            _meteredComponent = resolved;
            _meteredComponentExpiresAt = _clock() + _lifetime;
            return resolved;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Drops everything cached, forcing the next call to re-resolve from handles.</summary>
    public void Invalidate()
    {
        _productFamilyId = null;
        _meteredComponent = null;
        _productFamilyIdExpiresAt = default;
        _meteredComponentExpiresAt = default;
    }

    private bool TryReadProductFamilyId(out int value)
    {
        var cached = _productFamilyId;
        if (cached.HasValue && _clock() < _productFamilyIdExpiresAt)
        {
            value = cached.Value;
            return true;
        }

        value = default;
        return false;
    }

    private bool TryReadMeteredComponent(out MeteredComponent value)
    {
        var cached = _meteredComponent;
        if (cached is not null && _clock() < _meteredComponentExpiresAt)
        {
            value = cached;
            return true;
        }

        value = default!;
        return false;
    }
}
