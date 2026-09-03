using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.Infrastructure.Payments;

public sealed class PaymentOperationLock
{
    private readonly SemaphoreSlim[] _stripes = Enumerable.Range(0, 257)
        .Select(_ => new SemaphoreSlim(1, 1)).ToArray();

    public async Task<IDisposable> AcquireAsync(string key, CancellationToken ct)
    {
        var hash = StringComparer.Ordinal.GetHashCode(key) & int.MaxValue;
        var gate = _stripes[hash % _stripes.Length];
        await gate.WaitAsync(ct);
        return new Releaser(gate);
    }

    private sealed class Releaser(SemaphoreSlim gate) : IDisposable
    {
        public void Dispose() => gate.Release();
    }
}
