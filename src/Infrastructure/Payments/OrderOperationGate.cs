using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.Infrastructure.Payments;

public class OrderOperationGate
{
    private readonly ConcurrentDictionary<int, SemaphoreSlim> _gates = new();

    public async Task<T> RunAsync<T>(int orderId, Func<Task<T>> action)
    {
        var gate = _gates.GetOrAdd(orderId, _ => new SemaphoreSlim(1, 1));
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

    public async Task RunAsync(int orderId, Func<Task> action)
    {
        await RunAsync(orderId, async () =>
        {
            await action();
            return true;
        });
    }
}
