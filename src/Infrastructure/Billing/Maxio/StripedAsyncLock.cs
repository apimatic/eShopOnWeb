using System;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.Infrastructure.Billing.Maxio;

/// <summary>
/// Serialises work per key inside one process using a fixed set of lock stripes.
/// <para>
/// The subscribe flow is "check whether the shopper is already enrolled, then enroll" &#8212; a
/// read-then-write two concurrent requests could interleave. Maxio offers no conditional create,
/// so this lock closes the double-click window for the common case (both clicks land on the same
/// instance), while the re-check against Maxio taken inside the lock remains the authoritative
/// guard for retries and multi-instance deployments.
/// </para>
/// <para>
/// Striping is used rather than a per-key dictionary so memory stays bounded no matter how many
/// distinct subscribers pass through: two different keys can collide on a stripe, which costs a
/// brief, harmless serialisation and never a correctness problem.
/// </para>
/// </summary>
public sealed class StripedAsyncLock : IDisposable
{
    private readonly SemaphoreSlim[] _stripes;
    private bool _disposed;

    public StripedAsyncLock(int stripeCount = 64)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(stripeCount, 1);

        _stripes = new SemaphoreSlim[stripeCount];
        for (var i = 0; i < stripeCount; i++)
        {
            _stripes[i] = new SemaphoreSlim(1, 1);
        }
    }

    /// <summary>
    /// Acquires the stripe for <paramref name="key"/>, waiting at most <paramref name="timeout"/>.
    /// Dispose the returned handle to release it.
    /// </summary>
    /// <exception cref="TimeoutException">The lock could not be acquired within the timeout.</exception>
    public async Task<IDisposable> AcquireAsync(string key, TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ObjectDisposedException.ThrowIf(_disposed, this);

        var semaphore = _stripes[StripeFor(key)];

        if (!await semaphore.WaitAsync(timeout, cancellationToken))
        {
            throw new TimeoutException(
                $"Timed out after {timeout.TotalSeconds:0.##}s waiting for the billing lock on '{key}'.");
        }

        return new Handle(semaphore);
    }

    /// <summary>
    /// Maps a key onto a stripe. SHA-256 rather than <see cref="string.GetHashCode()"/> so the
    /// mapping is stable across processes and unaffected by hash randomisation, which keeps the
    /// behaviour reproducible in tests.
    /// </summary>
    private int StripeFor(string key)
    {
        Span<byte> hash = stackalloc byte[32];
        SHA256.HashData(Encoding.UTF8.GetBytes(key), hash);

        var index = BitConverter.ToUInt32(hash[..4]) % (uint)_stripes.Length;
        return (int)index;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        foreach (var semaphore in _stripes)
        {
            semaphore.Dispose();
        }
    }

    private sealed class Handle : IDisposable
    {
        private SemaphoreSlim? _semaphore;

        public Handle(SemaphoreSlim semaphore) => _semaphore = semaphore;

        public void Dispose() => Interlocked.Exchange(ref _semaphore, null)?.Release();
    }
}
