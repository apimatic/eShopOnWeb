using System.Collections.Concurrent;
using System.Threading;

namespace Microsoft.eShopWeb.Infrastructure.Services;

public sealed class ResendCoordinator
{
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new();
    public SemaphoreSlim For(int notificationId, string key) =>
        _locks.GetOrAdd($"{notificationId}:{key}", _ => new SemaphoreSlim(1, 1));
}
