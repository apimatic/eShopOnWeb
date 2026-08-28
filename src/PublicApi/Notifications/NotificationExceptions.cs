using System;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.PublicApi.Notifications;

public sealed class ApiRequestException(int statusCode, string safeMessage) : Exception(safeMessage)
{
    public int StatusCode { get; } = statusCode;
}

public sealed class NotificationIdempotencyLock
{
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, SemaphoreSlim> _locks = new();

    public async Task<IDisposable> AcquireAsync(Guid originalNotificationId, string key, CancellationToken cancellationToken)
    {
        var lockKey = $"{originalNotificationId:N}:{key}";
        var gate = _locks.GetOrAdd(lockKey, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        return new Releaser(gate);
    }

    private sealed class Releaser(SemaphoreSlim gate) : IDisposable
    {
        private SemaphoreSlim? _gate = gate;
        public void Dispose() => Interlocked.Exchange(ref _gate, null)?.Release();
    }
}
