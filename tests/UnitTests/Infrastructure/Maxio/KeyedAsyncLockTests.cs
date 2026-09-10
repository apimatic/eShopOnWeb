using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.Infrastructure.Services.Maxio;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Maxio;

public class KeyedAsyncLockTests
{
    [Fact]
    public async Task SameKey_SecondAcquireBlocksUntilFirstReleased()
    {
        var sut = new KeyedAsyncLock();

        var first = await sut.AcquireAsync("user@example.com", CancellationToken.None);
        var second = sut.AcquireAsync("user@example.com", CancellationToken.None);

        Assert.False(second.IsCompleted); // blocked while first is held

        first.Dispose();

        var releaser = await second; // now obtainable
        releaser.Dispose();
    }

    [Fact]
    public async Task DifferentKeys_DoNotBlockEachOther()
    {
        var sut = new KeyedAsyncLock();

        using var a = await sut.AcquireAsync("a@example.com", CancellationToken.None);
        var bTask = sut.AcquireAsync("b@example.com", CancellationToken.None);

        Assert.True(bTask.IsCompleted); // independent key is not blocked
        (await bTask).Dispose();
    }

    [Fact]
    public async Task SerializesConcurrentWorkForSameKey()
    {
        var sut = new KeyedAsyncLock();
        int concurrent = 0;
        int maxObserved = 0;

        async Task Worker()
        {
            using (await sut.AcquireAsync("k", CancellationToken.None))
            {
                var now = Interlocked.Increment(ref concurrent);
                maxObserved = System.Math.Max(maxObserved, now);
                await Task.Delay(20);
                Interlocked.Decrement(ref concurrent);
            }
        }

        var workers = new Task[8];
        for (int i = 0; i < workers.Length; i++)
        {
            workers[i] = Worker();
        }
        await Task.WhenAll(workers);

        Assert.Equal(1, maxObserved); // never more than one at a time
    }
}
