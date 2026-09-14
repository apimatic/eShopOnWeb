using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Maxio;

public class KeyedAsyncLockTests
{
    [Fact]
    public async Task SerialisesWorkSharingAKey()
    {
        var keyedLock = new KeyedAsyncLock();
        var concurrent = 0;
        var peak = 0;

        await Task.WhenAll(Enumerable.Range(0, 16).Select(async _ =>
        {
            using (await keyedLock.AcquireAsync("shopper"))
            {
                peak = Math.Max(peak, Interlocked.Increment(ref concurrent));
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
        using var held = await keyedLock.AcquireAsync("shopper-a");

        var other = keyedLock.AcquireAsync("shopper-b");

        Assert.Same(other, await Task.WhenAny(other, Task.Delay(TimeSpan.FromSeconds(5))));
        (await other).Dispose();
    }

    [Fact]
    public async Task TreatsKeysCaseInsensitivelySoOneShopperCannotRaceThemselves()
    {
        var keyedLock = new KeyedAsyncLock();
        using var held = await keyedLock.AcquireAsync("Shopper@Example.com");

        var contender = keyedLock.AcquireAsync("shopper@example.com");

        Assert.NotSame(contender, await Task.WhenAny(contender, Task.Delay(100)));
    }

    [Fact]
    public async Task ReleasesTheKeyOnceNobodyIsWaiting()
    {
        var keyedLock = new KeyedAsyncLock();

        for (var i = 0; i < 3; i++)
        {
            using var held = await keyedLock.AcquireAsync("shopper");
        }

        // Re-acquiring after the entry was cleaned up must still work.
        using var again = await keyedLock.AcquireAsync("shopper");
        Assert.NotNull(again);
    }

    [Fact]
    public async Task StopsWaitingWhenTheRequestIsCancelled()
    {
        var keyedLock = new KeyedAsyncLock();
        using var held = await keyedLock.AcquireAsync("shopper");
        using var cancellation = new CancellationTokenSource();

        var contender = keyedLock.AcquireAsync("shopper", cancellation.Token);
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => contender);
    }

    [Fact]
    public async Task DisposingTwiceIsHarmless()
    {
        var keyedLock = new KeyedAsyncLock();
        var releaser = await keyedLock.AcquireAsync("shopper");

        releaser.Dispose();
        releaser.Dispose();

        Assert.NotNull(await keyedLock.AcquireAsync("shopper"));
    }
}
