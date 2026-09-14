using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

internal sealed class KeyedLock
{
    private sealed class RefCounted
    {
        public int RefCount;
        public readonly SemaphoreSlim Semaphore = new(1, 1);
    }

    private sealed class Releaser : IDisposable
    {
        private readonly KeyedLock _owner;
        private readonly string _key;
        private readonly RefCounted _refCounted;
        private int _disposed;

        public Releaser(KeyedLock owner, string key, RefCounted refCounted)
        {
            _owner = owner;
            _key = key;
            _refCounted = refCounted;
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                _owner.Release(_key, _refCounted);
            }
        }
    }

    private readonly object _gate = new();
    private readonly Dictionary<string, RefCounted> _map = new();

    public async Task<IDisposable> LockAsync(string key, CancellationToken cancellationToken)
    {
        RefCounted refCounted;
        lock (_gate)
        {
            if (!_map.TryGetValue(key, out refCounted!))
            {
                refCounted = new RefCounted();
                _map.Add(key, refCounted);
            }

            refCounted.RefCount++;
        }

        try
        {
            await refCounted.Semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            ReleaseReservation(key, refCounted);
            throw;
        }

        return new Releaser(this, key, refCounted);
    }

    private void Release(string key, RefCounted refCounted)
    {
        refCounted.Semaphore.Release();
        ReleaseReservation(key, refCounted);
    }

    private void ReleaseReservation(string key, RefCounted refCounted)
    {
        lock (_gate)
        {
            if (--refCounted.RefCount == 0)
            {
                _map.Remove(key);
            }
        }
    }
}
