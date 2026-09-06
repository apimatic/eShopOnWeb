using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

/// <summary>
/// An in-process, per-key mutex.
/// </summary>
/// <remarks>
/// <para>
/// This closes the check-then-act window for every request served by <em>this</em> process, which is what a
/// double-clicking shopper actually produces. It does <strong>not</strong> coordinate across instances: to
/// make the guard hold behind a load balancer, register a distributed implementation of
/// <see cref="IBillingOperationLock"/> (a database row lock, or a lease in a shared cache) in its place —
/// nothing else in the integration has to change.
/// </para>
/// <para>
/// Entries are reference-counted and removed once released, so the map cannot grow without bound.
/// </para>
/// </remarks>
public sealed class InProcessBillingOperationLock : IBillingOperationLock
{
    private sealed class Entry
    {
        public readonly SemaphoreSlim Semaphore = new(1, 1);
        public int Waiters;
    }

    private readonly Dictionary<string, Entry> _entries = new(StringComparer.Ordinal);
    private readonly object _sync = new();

    public async Task<IDisposable> AcquireAsync(string key, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(key)) throw new ArgumentException("A lock key is required.", nameof(key));

        Entry entry;
        lock (_sync)
        {
            if (!_entries.TryGetValue(key, out entry!))
            {
                entry = new Entry();
                _entries.Add(key, entry);
            }

            entry.Waiters++;
        }

        try
        {
            await entry.Semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            Release(key, entry, held: false);
            throw;
        }

        return new Releaser(this, key, entry);
    }

    private void Release(string key, Entry entry, bool held)
    {
        if (held) entry.Semaphore.Release();

        lock (_sync)
        {
            if (--entry.Waiters <= 0 && _entries.TryGetValue(key, out var current) && ReferenceEquals(current, entry))
            {
                _entries.Remove(key);
                entry.Semaphore.Dispose();
            }
        }
    }

    private sealed class Releaser : IDisposable
    {
        private readonly InProcessBillingOperationLock _owner;
        private readonly string _key;
        private readonly Entry _entry;
        private int _disposed;

        public Releaser(InProcessBillingOperationLock owner, string key, Entry entry)
        {
            _owner = owner;
            _key = key;
            _entry = entry;
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
            _owner.Release(_key, _entry, held: true);
        }
    }
}
