using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

/// <summary>
/// Serialises work per key within a single process. Used to collapse the double-click case, where
/// two subscribe requests for the same shopper arrive close enough together that both would
/// otherwise observe "no subscription yet" before either has created one.
/// </summary>
/// <remarks>
/// This is a latency optimisation, not the correctness boundary. Correctness across processes comes
/// from the unique subscription reference the provider enforces, and from re-reading the provider
/// state after a uniqueness rejection. Register as a singleton.
/// </remarks>
public sealed class KeyedAsyncLock
{
    private readonly ConcurrentDictionary<string, Entry> _entries = new(StringComparer.Ordinal);

    public async Task<IDisposable> AcquireAsync(string key, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(key);

        while (true)
        {
            var entry = _entries.GetOrAdd(key, _ => new Entry());

            // The entry may have been retired by a releaser between GetOrAdd and TryRetain. Yield so
            // the releaser can finish evicting it, then take a fresh one.
            if (!entry.TryRetain())
            {
                await Task.Yield();
                continue;
            }

            try
            {
                await entry.Semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                Release(key, entry, semaphoreHeld: false);
                throw;
            }

            return new Releaser(this, key, entry);
        }
    }

    private void Release(string key, Entry entry, bool semaphoreHeld)
    {
        if (semaphoreHeld)
        {
            entry.Semaphore.Release();
        }

        if (entry.ReleaseAndCheckIdle())
        {
            _entries.TryRemove(new System.Collections.Generic.KeyValuePair<string, Entry>(key, entry));
        }
    }

    private sealed class Entry
    {
        private int _refCount;
        private bool _retired;

        public SemaphoreSlim Semaphore { get; } = new(1, 1);

        public bool TryRetain()
        {
            lock (this)
            {
                if (_retired)
                {
                    return false;
                }

                _refCount++;
                return true;
            }
        }

        public bool ReleaseAndCheckIdle()
        {
            lock (this)
            {
                if (--_refCount > 0)
                {
                    return false;
                }

                _retired = true;
                return true;
            }
        }
    }

    private sealed class Releaser : IDisposable
    {
        private readonly KeyedAsyncLock _owner;
        private readonly string _key;
        private readonly Entry _entry;
        private int _disposed;

        public Releaser(KeyedAsyncLock owner, string key, Entry entry)
        {
            _owner = owner;
            _key = key;
            _entry = entry;
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                _owner.Release(_key, _entry, semaphoreHeld: true);
            }
        }
    }
}
