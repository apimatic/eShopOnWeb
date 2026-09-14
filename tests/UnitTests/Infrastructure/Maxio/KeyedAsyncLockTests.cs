using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Maxio;

public class KeyedAsyncLockTests
{
    [Fact]
    public async Task ConcurrentHoldersOfTheSameKeyNeverOverlap()
    {
        var keyedLock = new KeyedAsyncLock();
        var inside = 0;
        var maxObserved = 0;

        await Task.WhenAll(Enumerable.Range(0, 32).Select(async _ =>
        {
            using (await keyedLock.AcquireAsync("shopper|plan"))
            {
                var current = Interlocked.Increment(ref inside);
                InterlockedMax(ref maxObserved, current);
                await Task.Delay(2);
                Interlocked.Decrement(ref inside);
            }
        }));

        Assert.Equal(1, maxObserved);
    }

    [Fact]
    public async Task DifferentKeysDoNotBlockEachOther()
    {
        var keyedLock = new KeyedAsyncLock();

        using var first = await keyedLock.AcquireAsync("shopper-a|plan");
        var second = keyedLock.AcquireAsync("shopper-b|plan");

        Assert.True(second.IsCompleted);
        (await second).Dispose();
    }

    [Fact]
    public async Task TheLockIsReentrantAcrossSequentialAcquisitions()
    {
        var keyedLock = new KeyedAsyncLock();

        for (var i = 0; i < 5; i++)
        {
            using var handle = await keyedLock.AcquireAsync("shopper|plan");
            Assert.NotNull(handle);
        }
    }

    [Fact]
    public async Task DisposingTwiceDoesNotOverReleaseTheLock()
    {
        var keyedLock = new KeyedAsyncLock();

        var handle = await keyedLock.AcquireAsync("shopper|plan");
        handle.Dispose();
        handle.Dispose();

        // If the double dispose had leaked a permit, two holders could enter at once.
        using var first = await keyedLock.AcquireAsync("shopper|plan");
        var second = keyedLock.AcquireAsync("shopper|plan");

        Assert.False(second.IsCompleted);

        first.Dispose();
        (await second).Dispose();
    }

    private static void InterlockedMax(ref int target, int value)
    {
        int current;
        while (value > (current = Volatile.Read(ref target)))
        {
            if (Interlocked.CompareExchange(ref target, value, current) == current)
            {
                return;
            }
        }
    }
}
