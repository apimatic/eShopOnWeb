using Microsoft.eShopWeb.Infrastructure.Billing.Maxio;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Billing.Maxio;

public class KeyedAsyncLockTests
{
    [Fact]
    public async Task SameKey_RunsOneAtATime()
    {
        var keyedLock = new KeyedAsyncLock();
        var concurrent = 0;
        var peak = 0;

        async Task Contend()
        {
            using (await keyedLock.AcquireAsync("subscriber", CancellationToken.None))
            {
                var now = Interlocked.Increment(ref concurrent);
                peak = Math.Max(peak, now);
                await Task.Delay(20);
                Interlocked.Decrement(ref concurrent);
            }
        }

        await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Contend()));

        Assert.Equal(1, peak);
    }

    [Fact]
    public async Task DifferentKeys_DoNotBlockEachOther()
    {
        var keyedLock = new KeyedAsyncLock();
        var first = await keyedLock.AcquireAsync("a", CancellationToken.None);

        var second = keyedLock.AcquireAsync("b", CancellationToken.None);
        var completed = await Task.WhenAny(second, Task.Delay(TimeSpan.FromSeconds(2)));

        Assert.Same(second, completed);

        first.Dispose();
        (await second).Dispose();
    }

    [Fact]
    public async Task ReleasedKeys_AreNotLeaked()
    {
        var keyedLock = new KeyedAsyncLock();

        for (var i = 0; i < 100; i++)
        {
            using (await keyedLock.AcquireAsync($"subscriber-{i}", CancellationToken.None))
            {
            }
        }

        // Re-acquiring a key that was fully released must not deadlock on a stale semaphore.
        using var reacquired = await keyedLock.AcquireAsync("subscriber-0", CancellationToken.None);
        Assert.NotNull(reacquired);
    }
}
