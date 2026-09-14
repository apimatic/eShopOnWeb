using Microsoft.eShopWeb.Infrastructure.Maxio;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Maxio;

public class KeyedAsyncLockTests
{
    [Fact]
    public async Task HoldersOfTheSameKeyRunOneAtATime()
    {
        var locks = new KeyedAsyncLock();
        var inCriticalSection = 0;
        var peak = 0;

        await Task.WhenAll(Enumerable.Range(0, 16).Select(async _ =>
        {
            using (await locks.AcquireAsync("shopper", CancellationToken.None))
            {
                peak = Math.Max(peak, Interlocked.Increment(ref inCriticalSection));
                await Task.Delay(5);
                Interlocked.Decrement(ref inCriticalSection);
            }
        }));

        Assert.Equal(1, peak);
    }

    [Fact]
    public async Task DifferentKeysDoNotBlockEachOther()
    {
        var locks = new KeyedAsyncLock();
        using var first = await locks.AcquireAsync("shopper-a", CancellationToken.None);

        var second = locks.AcquireAsync("shopper-b", CancellationToken.None);
        var completed = await Task.WhenAny(second, Task.Delay(TimeSpan.FromSeconds(2)));

        Assert.Same(second, completed);
        (await second).Dispose();
    }

    [Fact]
    public async Task AKeyCanBeReacquiredAfterItIsReleased()
    {
        var locks = new KeyedAsyncLock();

        for (var attempt = 0; attempt < 100; attempt++)
        {
            using var holder = await locks.AcquireAsync("shopper", CancellationToken.None);
        }
    }

    [Fact]
    public async Task AWaitingCallerIsReleasedWhenTheHolderFinishes()
    {
        var locks = new KeyedAsyncLock();
        var holder = await locks.AcquireAsync("shopper", CancellationToken.None);

        var waiting = locks.AcquireAsync("shopper", CancellationToken.None);
        Assert.False(waiting.IsCompleted);

        holder.Dispose();

        (await waiting.WaitAsync(TimeSpan.FromSeconds(5))).Dispose();
    }
}
