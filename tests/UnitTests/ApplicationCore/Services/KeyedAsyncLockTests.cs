using Microsoft.eShopWeb.ApplicationCore.Services;

using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Services;

public class KeyedAsyncLockTests
{
    [Fact]
    public async Task SerialisesWorkThatSharesAKey()
    {
        var keyedLock = new KeyedAsyncLock();
        var concurrent = 0;
        var peak = 0;

        async Task Work()
        {
            using (await keyedLock.AcquireAsync("shopper"))
            {
                var current = Interlocked.Increment(ref concurrent);
                peak = Math.Max(peak, current);
                await Task.Delay(5);
                Interlocked.Decrement(ref concurrent);
            }
        }

        await Task.WhenAll(Enumerable.Range(0, 16).Select(_ => Work()));

        Assert.Equal(1, peak);
    }

    [Fact]
    public async Task LetsDifferentKeysRunAtTheSameTime()
    {
        var keyedLock = new KeyedAsyncLock();
        using var first = await keyedLock.AcquireAsync("shopper-a");

        var second = keyedLock.AcquireAsync("shopper-b");

        Assert.Same(second, await Task.WhenAny(second, Task.Delay(1000).ContinueWith(_ => (IDisposable)null!)));
        (await second).Dispose();
    }

    [Fact]
    public async Task ReleasesTheKeyWhenTheHolderIsDisposed()
    {
        var keyedLock = new KeyedAsyncLock();

        (await keyedLock.AcquireAsync("shopper")).Dispose();

        using var reacquired = await keyedLock.AcquireAsync("shopper").WaitAsync(TimeSpan.FromSeconds(1));
        Assert.NotNull(reacquired);
    }

    [Fact]
    public async Task StopsWaitingWhenTheCallerCancels()
    {
        var keyedLock = new KeyedAsyncLock();
        using var held = await keyedLock.AcquireAsync("shopper");
        using var cancellation = new CancellationTokenSource(50);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => keyedLock.AcquireAsync("shopper", cancellation.Token));
    }
}
