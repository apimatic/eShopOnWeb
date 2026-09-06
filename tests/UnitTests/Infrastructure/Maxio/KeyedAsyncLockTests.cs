using Microsoft.eShopWeb.Infrastructure.Maxio;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Maxio;

public class KeyedAsyncLockTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    [Fact]
    public async Task AcquireAsync_LetsOnlyOneHolderAtATimeThroughForTheSameKey()
    {
        var locks = new KeyedAsyncLock();
        var concurrent = 0;
        var peak = 0;

        await Task.WhenAll(Enumerable.Range(0, 8).Select(async _ =>
        {
            using (await locks.AcquireAsync("same-key"))
            {
                peak = Math.Max(peak, Interlocked.Increment(ref concurrent));
                await Task.Delay(10);
                Interlocked.Decrement(ref concurrent);
            }
        }));

        Assert.Equal(1, peak);
    }

    [Fact]
    public async Task AcquireAsync_DoesNotBlockDifferentKeys()
    {
        var locks = new KeyedAsyncLock();
        var aInside = new TaskCompletionSource();
        var bInside = new TaskCompletionSource();

        // Each holder waits for the other to get inside before letting go, so this only completes if
        // the two keys really are independent.
        var a = Hold("key-a", aInside, bInside.Task);
        var b = Hold("key-b", bInside, aInside.Task);

        await Task.WhenAll(a, b).WaitAsync(Timeout);

        async Task Hold(string key, TaskCompletionSource entered, Task other)
        {
            using (await locks.AcquireAsync(key))
            {
                entered.SetResult();
                await other;
            }
        }
    }

    [Fact]
    public async Task AcquireAsync_ReleasesTheKeyForReuseAfterTheLastHolderIsDone()
    {
        var locks = new KeyedAsyncLock();

        for (var i = 0; i < 3; i++)
        {
            using var releaser = await locks.AcquireAsync("recycled");
        }

        // A key whose bookkeeping leaked would deadlock here rather than let this through.
        using var final = await locks.AcquireAsync("recycled").WaitAsync(Timeout);
    }

    [Fact]
    public async Task Dispose_IsIdempotent()
    {
        var locks = new KeyedAsyncLock();

        var releaser = await locks.AcquireAsync("key");
        releaser.Dispose();
        releaser.Dispose();

        // A double release would have over-counted the semaphore and let two holders in at once.
        using var first = await locks.AcquireAsync("key").WaitAsync(Timeout);
        var second = locks.AcquireAsync("key");

        Assert.False(second.IsCompleted);
    }
}
