using System;
using System.Collections.Concurrent;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

/// <summary>Registered as a singleton so previews survive across the (scoped) SubscriptionService instances that issue and consume them.</summary>
public class PlanChangePreviewCache : IPlanChangePreviewCache
{
    private readonly ConcurrentDictionary<Guid, PlanChangePreviewEntry> _entries = new();

    public Guid Store(PlanChangePreviewEntry entry)
    {
        PurgeExpired();
        var token = Guid.NewGuid();
        _entries[token] = entry;
        return token;
    }

    public PlanChangePreviewEntry? TryConsume(Guid token)
    {
        if (!_entries.TryRemove(token, out var entry))
        {
            return null;
        }

        return entry.ExpiresAtUtc >= DateTimeOffset.UtcNow ? entry : null;
    }

    private void PurgeExpired()
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var kvp in _entries)
        {
            if (kvp.Value.ExpiresAtUtc < now)
            {
                _entries.TryRemove(kvp.Key, out _);
            }
        }
    }
}
