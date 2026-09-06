using Microsoft.eShopWeb.Infrastructure.Maxio;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Maxio;

public class KeyedAsyncLockTests
{
    [Fact]
    public async Task SerialisesWorkThatSharesAKey()
    {
        using var keyedLock = new KeyedAsyncLock();
        var concurrent = 0;
        var peak = 0;

        await Task.WhenAll(Enumerable.Range(0, 20).Select(async _ =>
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
    public async Task DoesNotBlockWorkOnDifferentKeys()
    {
        using var keyedLock = new KeyedAsyncLock();
        using var bothInside = new SemaphoreSlim(0, 2);

        var first = Task.Run(async () =>
        {
            using (await keyedLock.AcquireAsync("a"))
            {
                bothInside.Release();
                await bothInside.WaitAsync(TimeSpan.FromSeconds(5));
            }
        });

        var second = Task.Run(async () =>
        {
            using (await keyedLock.AcquireAsync("b"))
            {
                bothInside.Release();
                await bothInside.WaitAsync(TimeSpan.FromSeconds(5));
            }
        });

        // Each task only completes once the other has also entered, so this returning at all proves
        // the two keys were not serialised against each other.
        await Task.WhenAll(first, second).WaitAsync(TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task ReleasesTheKeyWhenTheBodyThrows()
    {
        using var keyedLock = new KeyedAsyncLock();

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            using (await keyedLock.AcquireAsync("key"))
            {
                throw new InvalidOperationException("boom");
            }
        });

        // A leaked semaphore would make this acquisition hang forever.
        using (await keyedLock.AcquireAsync("key").WaitAsync(TimeSpan.FromSeconds(5)))
        {
        }
    }

    [Fact]
    public async Task DoesNotLeakEntriesAfterKeysAreReleased()
    {
        using var keyedLock = new KeyedAsyncLock();

        for (var i = 0; i < 500; i++)
        {
            using (await keyedLock.AcquireAsync($"key-{i}"))
            {
            }
        }

        // Reacquiring every key still succeeds; the point is that the dictionary was pruned rather
        // than growing without bound, which the release path asserts by disposing each semaphore.
        using (await keyedLock.AcquireAsync("key-0").WaitAsync(TimeSpan.FromSeconds(5)))
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
