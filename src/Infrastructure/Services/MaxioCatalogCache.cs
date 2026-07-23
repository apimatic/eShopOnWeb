using System;
using System.Collections.Concurrent;

namespace Microsoft.eShopWeb.Infrastructure.Services;

/// <summary>
/// A tiny time-bounded cache for the Maxio catalog identifiers the billing client resolves from handles.
/// Maxio's numeric ids are stable for the lifetime of a seed but not across one, so they are cached for a
/// short window rather than for the process lifetime (plan.md §1.3). Registered as a singleton so the
/// lookup is not repeated for every request.
/// </summary>
public sealed class MaxioCatalogCache
{
    private readonly ConcurrentDictionary<string, Entry> _entries = new(StringComparer.Ordinal);

    /// <summary>Returns a cached value when one is present and has not expired.</summary>
    public bool TryGet<T>(string key, out T value)
    {
        if (_entries.TryGetValue(key, out var entry) && entry.ExpiresAt > DateTimeOffset.UtcNow && entry.Value is T typed)
        {
            value = typed;
            return true;
        }

        value = default!;
        return false;
    }

    /// <summary>Stores a value for <paramref name="duration"/>. A non-positive duration disables caching.</summary>
    public void Set<T>(string key, T value, TimeSpan duration)
    {
        if (duration <= TimeSpan.Zero || value is null)
        {
            _entries.TryRemove(key, out _);
            return;
        }

        _entries[key] = new Entry(value, DateTimeOffset.UtcNow.Add(duration));
    }

    /// <summary>Drops every cached entry — used when a lookup proves an id has gone stale.</summary>
    public void Clear() => _entries.Clear();

    private sealed record Entry(object Value, DateTimeOffset ExpiresAt);
}
