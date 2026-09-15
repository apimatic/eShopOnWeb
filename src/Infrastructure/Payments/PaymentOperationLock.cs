using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.Infrastructure.Payments;

public sealed class PaymentOperationLock
{
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new();
    private readonly ConcurrentDictionary<string, string> _vaultRequestIds = new();
    private readonly ConcurrentDictionary<string, string> _vaultFingerprintsByToken = new();

    public string GetOrCreateVaultRequestId(string fingerprint) =>
        _vaultRequestIds.GetOrAdd(fingerprint, _ => $"eshop-vault-{Guid.NewGuid():N}");

    public void RememberVaultToken(string tokenId, string fingerprint) =>
        _vaultFingerprintsByToken[tokenId] = fingerprint;

    public void ForgetVaultToken(string tokenId)
    {
        if (_vaultFingerprintsByToken.TryRemove(tokenId, out var fingerprint))
            _vaultRequestIds.TryRemove(fingerprint, out _);
    }

    public async Task<IDisposable> AcquireAsync(string key, CancellationToken cancellationToken)
    {
        var semaphore = _locks.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));
        await semaphore.WaitAsync(cancellationToken);
        return new Releaser(semaphore);
    }

    private sealed class Releaser : IDisposable
    {
        private readonly SemaphoreSlim _semaphore;
        private int _released;
        public Releaser(SemaphoreSlim semaphore) => _semaphore = semaphore;
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _released, 1) == 0) _semaphore.Release();
        }
    }
}
