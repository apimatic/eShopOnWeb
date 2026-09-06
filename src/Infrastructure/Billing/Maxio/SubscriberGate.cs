using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.Infrastructure.Billing.Maxio;

/// <summary>
/// Serialises billing writes per shopper, so a double-clicked Subscribe button does its
/// read-then-create once rather than twice in parallel.
/// </summary>
/// <remarks>
/// This is a fast path, not the guarantee: it only covers one process. Correctness across instances comes
/// from the unique references in <see cref="MaxioReferenceFactory"/>, which Advanced Billing enforces
/// server-side. Shoppers are gated independently, so one slow enrolment never blocks another shopper.
/// </remarks>
internal sealed class SubscriberGate : IDisposable
{
    private readonly Dictionary<string, Entry> _entries = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _sync = new();
    private bool _disposed;

    /// <summary>
    /// Acquires the gate for <paramref name="key"/>. Dispose the returned handle to release it.
    /// </summary>
    public async Task<IDisposable> AcquireAsync(string key, CancellationToken cancellationToken)
    {
        Entry entry;

        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            if (!_entries.TryGetValue(key, out entry!))
            {
                entry = new Entry();
                _entries[key] = entry;
            }

            entry.Waiters++;
        }

        try
        {
            await entry.Semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            Release(key, entry, acquired: false);
            throw;
        }

        return new Handle(this, key, entry);
    }

    private void Release(string key, Entry entry, bool acquired)
    {
        if (acquired)
        {
            entry.Semaphore.Release();
        }

        lock (_sync)
        {
            entry.Waiters--;

            // Drop the entry once nobody is holding or queued for it, so the map tracks live contention
            // rather than growing with every shopper the process has ever served.
            if (entry.Waiters == 0 && _entries.TryGetValue(key, out var current) && ReferenceEquals(current, entry))
            {
                _entries.Remove(key);
                entry.Semaphore.Dispose();
            }
        }
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;

            foreach (var entry in _entries.Values)
            {
                entry.Semaphore.Dispose();
            }

            _entries.Clear();
        }
    }

    private sealed class Entry
    {
        public SemaphoreSlim Semaphore { get; } = new(1, 1);

        public int Waiters { get; set; }
    }

    private sealed class Handle : IDisposable
    {
        private readonly SubscriberGate _owner;
        private readonly string _key;
        private readonly Entry _entry;
        private bool _released;

        public Handle(SubscriberGate owner, string key, Entry entry)
        {
            _owner = owner;
            _key = key;
            _entry = entry;
        }

        public void Dispose()
        {
            if (_released)
            {
                return;
            }

            _released = true;
            _owner.Release(_key, _entry, acquired: true);
        }
    }
}
