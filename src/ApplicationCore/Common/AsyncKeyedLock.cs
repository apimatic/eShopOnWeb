using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Common;

/// <summary>
/// Per-key async mutex used to serialize payment operations on one order, so a
/// double-clicked authorize/capture/refund can never overlap itself in this process.
/// </summary>
public static class AsyncKeyedLock
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new();

    public static async Task<IDisposable> LockAsync(string key, CancellationToken ct = default)
    {
        var semaphore = _locks.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));
        await semaphore.WaitAsync(ct).ConfigureAwait(false);
        return new Releaser(key, semaphore);
    }

    private sealed class Releaser : IDisposable
    {
        private readonly string _key;
        private readonly SemaphoreSlim _semaphore;
        private bool _disposed;

        public Releaser(string key, SemaphoreSlim semaphore)
        {
            _key = key;
            _semaphore = semaphore;
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }
            _disposed = true;
            _semaphore.Release();
            _locks.TryRemove(_key, out _);
        }
    }
}
