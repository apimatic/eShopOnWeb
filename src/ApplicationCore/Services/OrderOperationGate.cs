using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

/// <summary>
/// Serializes pay/fulfil/cancel/refund for a given order so a double-click cannot
/// authorize or capture twice.
/// </summary>
public class OrderOperationGate
{
    private readonly ConcurrentDictionary<int, SemaphoreSlim> _locks = new();

    public async Task<T> RunAsync<T>(int orderId, Func<Task<T>> action)
    {
        var gate = _locks.GetOrAdd(orderId, static _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync();
        try
        {
            return await action();
        }
        finally
        {
            gate.Release();
        }
    }

    public Task RunAsync(int orderId, Func<Task> action) =>
        RunAsync(orderId, async () =>
        {
            await action();
            return true;
        });
}
