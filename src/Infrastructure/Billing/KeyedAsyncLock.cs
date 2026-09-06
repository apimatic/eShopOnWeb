using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

/// <summary>
/// Serialises work per key within this process, so two requests for the same shopper cannot
/// interleave the read-then-write halves of an idempotent operation.
/// </summary>
/// <remarks>
/// This is a latency optimisation over the provider-side guard, not a substitute for it: across
/// several instances the locks are independent, which is why every write to the billing provider
/// also carries its own duplicate protection.
/// </remarks>
public sealed class KeyedAsyncLock
{
    private readonly Dictionary<string, Entry> _entries = new(StringComparer.Ordinal);
    private readonly object _gate = new();

    /// <summary>Waits for exclusive access to <paramref name="key"/>; dispose the result to release.</summary>
    public async Task<Releaser> AcquireAsync(string key, CancellationToken cancellationToken = default)
    {
        Entry entry;
        lock (_gate)
        {
            if (!_entries.TryGetValue(key, out entry!))
            {
                entry = new Entry();
                _entries[key] = entry;
            }

            entry.RefCount++;
        }

        try
        {
            await entry.Semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            ReleaseRef(key, entry, entered: false);
            throw;
        }

        return new Releaser(this, key, entry);
    }

    private void ReleaseRef(string key, Entry entry, bool entered)
    {
        if (entered)
        {
            entry.Semaphore.Release();
        }

        lock (_gate)
        {
            // Once the last waiter leaves, drop the entry so the map does not grow by one per
            // shopper that has ever subscribed. Nobody can pick it up again: the only way in is
            // through this same lock.
            if (--entry.RefCount == 0)
            {
                _entries.Remove(key);
                entry.Semaphore.Dispose();
            }
        }
    }

    internal sealed class Entry
    {
        public SemaphoreSlim Semaphore { get; } = new(1, 1);

        public int RefCount { get; set; }
    }

    public readonly struct Releaser : IDisposable
    {
        private readonly KeyedAsyncLock? _owner;
        private readonly string _key;
        private readonly Entry _entry;

        internal Releaser(KeyedAsyncLock owner, string key, Entry entry)
        {
            _owner = owner;
            _key = key;
            _entry = entry;
        }

        public void Dispose() => _owner?.ReleaseRef(_key, _entry, entered: true);
    }
}
