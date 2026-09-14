using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.Infrastructure.Billing;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Billing;

public class KeyedAsyncLockTests
{
    [Fact]
    public async Task SerialisesWorkThatSharesAKey()
    {
        var keyedLock = new KeyedAsyncLock();
        var concurrent = 0;
        var peak = 0;

        await Task.WhenAll(Enumerable.Range(0, 16).Select(async _ =>
        {
            using (await keyedLock.AcquireAsync("same-key"))
            {
                var now = Interlocked.Increment(ref concurrent);
                InterlockedMax(ref peak, now);
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
        using var held = await keyedLock.AcquireAsync("one");

        // Would hang if the lock were global rather than per key.
        using var other = await keyedLock.AcquireAsync("two").WaitAsync(System.TimeSpan.FromSeconds(5));

        Assert.NotNull(other);
    }

    [Fact]
    public async Task ReleasesTheKeyAfterTheHandleIsDisposed()
    {
        var keyedLock = new KeyedAsyncLock();

        for (var i = 0; i < 3; i++)
        {
            using var handle = await keyedLock.AcquireAsync("key").WaitAsync(System.TimeSpan.FromSeconds(5));
            Assert.NotNull(handle);
        }
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
