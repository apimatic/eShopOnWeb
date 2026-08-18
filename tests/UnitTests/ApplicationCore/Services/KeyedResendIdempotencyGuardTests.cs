using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Services;

public class KeyedResendIdempotencyGuardTests
{
    [Fact]
    public async Task RunsTheActionAndReturnsItsResult()
    {
        var guard = new KeyedResendIdempotencyGuard();

        var result = await guard.RunExclusivelyAsync("k", () => Task.FromResult(42));

        Assert.Equal(42, result);
    }

    [Fact]
    public async Task SerializesConcurrentWorkForTheSameKey()
    {
        var guard = new KeyedResendIdempotencyGuard();
        var concurrent = 0;
        var maxObserved = 0;

        async Task<int> Body()
        {
            var now = Interlocked.Increment(ref concurrent);
            maxObserved = System.Math.Max(maxObserved, now);
            await Task.Delay(20);
            Interlocked.Decrement(ref concurrent);
            return 0;
        }

        await Task.WhenAll(
            guard.RunExclusivelyAsync("same", Body),
            guard.RunExclusivelyAsync("same", Body),
            guard.RunExclusivelyAsync("same", Body));

        Assert.Equal(1, maxObserved); // never two at once under the same key
    }

    [Fact]
    public async Task AllowsDifferentKeysToProceedIndependently()
    {
        var guard = new KeyedResendIdempotencyGuard();
        var gate = new SemaphoreSlim(0, 1);

        // First key parks inside the guard; a different key must not be blocked by it.
        var parked = guard.RunExclusivelyAsync("a", async () => { await gate.WaitAsync(); return 1; });
        var other = await guard.RunExclusivelyAsync("b", () => Task.FromResult(2));

        Assert.Equal(2, other);
        gate.Release();
        Assert.Equal(1, await parked);
    }
}
