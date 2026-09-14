using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.Infrastructure.Billing.Maxio;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Billing.Maxio;

public class KeyedAsyncLockTests
{
    [Fact]
    public async Task SerialisesWorkSharingAKey()
    {
        var locks = new KeyedAsyncLock();
        var concurrent = 0;
        var peak = 0;

        await Task.WhenAll(Enumerable.Range(0, 25).Select(async _ =>
        {
            using (await locks.AcquireAsync("shopper"))
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
    public async Task LetsDifferentKeysRunAtTheSameTime()
    {
        var locks = new KeyedAsyncLock();
        using var bothInside = new SemaphoreSlim(0, 2);

        var first = Task.Run(async () =>
        {
            using (await locks.AcquireAsync("a"))
            {
                bothInside.Release();
                await Task.Delay(50);
            }
        });

        var second = Task.Run(async () =>
        {
            using (await locks.AcquireAsync("b"))
            {
                bothInside.Release();
                await Task.Delay(50);
            }
        });

        Assert.True(await bothInside.WaitAsync(2000));
        Assert.True(await bothInside.WaitAsync(2000));

        await Task.WhenAll(first, second);
    }

    [Fact]
    public async Task CanBeReacquiredAfterEveryHolderHasReleased()
    {
        var locks = new KeyedAsyncLock();

        for (var i = 0; i < 50; i++)
        {
            using (await locks.AcquireAsync("shopper"))
            {
            }
        }
    }

    [Fact]
    public async Task ReleasingIsIdempotent()
    {
        var locks = new KeyedAsyncLock();

        var releaser = await locks.AcquireAsync("shopper");
        releaser.Dispose();
        releaser.Dispose();

        // Still acquirable, so the double dispose did not corrupt the entry.
        using (await locks.AcquireAsync("shopper"))
        {
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
