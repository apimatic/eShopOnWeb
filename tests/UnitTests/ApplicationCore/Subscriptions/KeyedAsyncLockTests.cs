using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Subscriptions;

public class KeyedAsyncLockTests
{
    [Fact]
    public async Task LetsOnlyOneHolderThroughPerKeyAtATime()
    {
        var keyedLock = new KeyedAsyncLock();
        var concurrent = 0;
        var peak = 0;

        await Task.WhenAll(Enumerable.Range(0, 16).Select(async _ =>
        {
            using (await keyedLock.AcquireAsync("shopper"))
            {
                var current = Interlocked.Increment(ref concurrent);
                InterlockedMax(ref peak, current);
                await Task.Delay(5);
                Interlocked.Decrement(ref concurrent);
            }
        }));

        Assert.Equal(1, peak);
    }

    [Fact]
    public async Task DoesNotBlockDifferentKeys()
    {
        var keyedLock = new KeyedAsyncLock();
        using var first = await keyedLock.AcquireAsync("shopper-one");

        var second = keyedLock.AcquireAsync("shopper-two");

        Assert.True(second.IsCompleted);
        second.Result.Dispose();
    }

    [Fact]
    public async Task ReleasesTheKeyOnceTheHolderIsDisposed()
    {
        var keyedLock = new KeyedAsyncLock();
        (await keyedLock.AcquireAsync("shopper")).Dispose();

        var again = keyedLock.AcquireAsync("shopper");

        Assert.True(again.IsCompleted);
        again.Result.Dispose();
    }

    [Fact]
    public async Task IgnoresARepeatedDispose()
    {
        var keyedLock = new KeyedAsyncLock();
        var holder = await keyedLock.AcquireAsync("shopper");

        holder.Dispose();
        holder.Dispose();

        // A double release would have handed the key to two holders at once.
        using var first = await keyedLock.AcquireAsync("shopper");
        var second = keyedLock.AcquireAsync("shopper");

        Assert.False(second.IsCompleted);
    }

    private static void InterlockedMax(ref int target, int value)
    {
        int seen;
        while ((seen = Volatile.Read(ref target)) < value
            && Interlocked.CompareExchange(ref target, value, seen) != seen)
        {
        }
    }
}
