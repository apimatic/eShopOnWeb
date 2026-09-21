using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.Infrastructure.Messaging;

/// <summary>
/// A process-wide idempotency guard: one lock per key, so concurrent re-send attempts under the same
/// caller key are serialised and the claim can be established atomically (check-then-act under the lock).
///
/// This is scoped to a single PublicApi host — the deployment model of this reference app, where each
/// host also owns its own in-memory store. A multi-host deployment would move the claim to shared,
/// durable storage; that is out of scope here and recorded as such.
/// </summary>
public sealed class InMemoryResendIdempotencyGuard : IResendIdempotencyGuard
{
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new();

    public async Task<T> ExecuteAsync<T>(string idempotencyKey, Func<CancellationToken, Task<T>> operation, CancellationToken ct)
    {
        var gate = _locks.GetOrAdd(idempotencyKey, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct);
        try
        {
            return await operation(ct);
        }
        finally
        {
            gate.Release();
        }
    }
}
