using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.Infrastructure.Subscriptions;

public sealed class SubscriptionOperationLock
{
    private readonly ConcurrentDictionary<string, LockEntry> _locks = new(StringComparer.Ordinal);

    public async ValueTask<IDisposable> AcquireAsync(string key, CancellationToken cancellationToken)
    {
        LockEntry entry;
        while (true)
        {
            entry = _locks.GetOrAdd(key, static _ => new LockEntry());
            lock (entry)
            {
                if (_locks.TryGetValue(key, out var current) && ReferenceEquals(current, entry))
                {
                    entry.References++;
                    break;
                }
            }
        }

        try
        {
            await entry.Gate.WaitAsync(cancellationToken);
            return new Releaser(this, key, entry);
        }
        catch
        {
            ReleaseReference(key, entry, releaseGate: false);
            throw;
        }
    }

    private void ReleaseReference(string key, LockEntry entry, bool releaseGate)
    {
        if (releaseGate)
        {
            entry.Gate.Release();
        }

        lock (entry)
        {
            entry.References--;
            if (entry.References == 0)
            {
                ((ICollection<KeyValuePair<string, LockEntry>>)_locks)
                    .Remove(new KeyValuePair<string, LockEntry>(key, entry));
                entry.Gate.Dispose();
            }
        }
    }

    private sealed class LockEntry
    {
        public SemaphoreSlim Gate { get; } = new(1, 1);
        public int References { get; set; }
    }

    private sealed class Releaser : IDisposable
    {
        private readonly SubscriptionOperationLock _owner;
        private readonly string _key;
        private LockEntry? _entry;

        public Releaser(SubscriptionOperationLock owner, string key, LockEntry entry)
        {
            _owner = owner;
            _key = key;
            _entry = entry;
        }

        public void Dispose()
        {
            var entry = Interlocked.Exchange(ref _entry, null);
            if (entry is null)
            {
                return;
            }
            _owner.ReleaseReference(_key, entry, releaseGate: true);
        }
    }
}
