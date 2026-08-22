using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Payments;

/// <summary>
/// Serializes money-moving operations per order so a double-click cannot authorize or capture twice.
/// </summary>
public sealed class OrderOperationGate
{
    private readonly ConcurrentDictionary<int, SemaphoreSlim> _gates = new();

    public async Task<T> RunAsync<T>(int orderId, Func<Task<T>> action, CancellationToken cancellationToken = default)
    {
        var gate = _gates.GetOrAdd(orderId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            return await action();
        }
        finally
        {
            gate.Release();
        }
    }

    public Task RunAsync(int orderId, Func<Task> action, CancellationToken cancellationToken = default)
    {
        return RunAsync(orderId, async () =>
        {
            await action();
            return 0;
        }, cancellationToken);
    }
}
