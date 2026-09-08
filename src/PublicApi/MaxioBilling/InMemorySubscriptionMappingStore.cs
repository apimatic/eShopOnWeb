using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.PublicApi.MaxioBilling;

public interface ISubscriptionMappingStore
{
    int? TryGetCustomerId(string userId);
    void SetCustomerId(string userId, int customerId);
    int? TryGetSubscriptionId(string subscriptionReference);
    void SetSubscriptionId(string subscriptionReference, int subscriptionId);
    Task<IDisposable> AcquireLockAsync(string key, CancellationToken ct);
}

public class InMemorySubscriptionMappingStore : ISubscriptionMappingStore
{
    private readonly ConcurrentDictionary<string, int> _customerIds = new();
    private readonly ConcurrentDictionary<string, int> _subscriptionIds = new();
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new();

    public int? TryGetCustomerId(string userId) =>
        _customerIds.TryGetValue(userId, out var customerId) ? customerId : null;

    public void SetCustomerId(string userId, int customerId) =>
        _customerIds[userId] = customerId;

    public int? TryGetSubscriptionId(string subscriptionReference) =>
        _subscriptionIds.TryGetValue(subscriptionReference, out var subscriptionId) ? subscriptionId : null;

    public void SetSubscriptionId(string subscriptionReference, int subscriptionId) =>
        _subscriptionIds[subscriptionReference] = subscriptionId;

    public async Task<IDisposable> AcquireLockAsync(string key, CancellationToken ct)
    {
        var semaphore = _locks.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));
        await semaphore.WaitAsync(ct);
        return new LockReleaser(semaphore);
    }

    private sealed class LockReleaser : IDisposable
    {
        private readonly SemaphoreSlim _semaphore;

        public LockReleaser(SemaphoreSlim semaphore)
        {
            _semaphore = semaphore;
        }

        public void Dispose() => _semaphore.Release();
    }
}
