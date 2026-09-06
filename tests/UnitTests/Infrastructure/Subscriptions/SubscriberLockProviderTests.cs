using Microsoft.eShopWeb.Infrastructure.Subscriptions.Maxio;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Subscriptions;

public class SubscriberLockProviderTests
{
    [Fact]
    public async Task SecondAcquireForTheSameSubscriberWaitsForTheFirstToRelease()
    {
        using var locks = new SubscriberLockProvider();

        var first = await locks.AcquireAsync("demouser@microsoft.com");
        var second = locks.AcquireAsync("demouser@microsoft.com");

        Assert.False(second.IsCompleted);

        first.Dispose();

        (await second.WaitAsync(TimeSpan.FromSeconds(5))).Dispose();
    }

    [Fact]
    public async Task DifferentSubscribersDoNotBlockEachOther()
    {
        using var locks = new SubscriberLockProvider();

        using var first = await locks.AcquireAsync("demouser@microsoft.com");
        var second = locks.AcquireAsync("admin@microsoft.com");

        using var acquired = await second.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.NotNull(acquired);
    }

    [Fact]
    public async Task ConcurrentSubscribersAreSerialisedOneAtATime()
    {
        using var locks = new SubscriberLockProvider();

        var concurrent = 0;
        var maxObserved = 0;

        await Task.WhenAll(Enumerable.Range(0, 16).Select(async _ =>
        {
            using (await locks.AcquireAsync("demouser@microsoft.com"))
            {
                var running = Interlocked.Increment(ref concurrent);
                maxObserved = Math.Max(maxObserved, running);
                await Task.Delay(5);
                Interlocked.Decrement(ref concurrent);
            }
        }));

        Assert.Equal(1, maxObserved);
    }

    [Fact]
    public async Task EntriesAreDroppedOnceNobodyHoldsOrWantsThem()
    {
        using var locks = new SubscriberLockProvider();

        foreach (var subscriber in new[] { "a@example.com", "b@example.com", "c@example.com" })
        {
            using (await locks.AcquireAsync(subscriber))
            {
                Assert.Equal(1, locks.TrackedKeyCount);
            }
        }

        Assert.Equal(0, locks.TrackedKeyCount);
    }

    [Fact]
    public async Task AnEntryIsKeptWhileSomeoneIsStillWaitingForIt()
    {
        using var locks = new SubscriberLockProvider();

        var held = await locks.AcquireAsync("demouser@microsoft.com");
        var waiting = locks.AcquireAsync("demouser@microsoft.com");

        held.Dispose();

        (await waiting.WaitAsync(TimeSpan.FromSeconds(5))).Dispose();
        Assert.Equal(0, locks.TrackedKeyCount);
    }

    [Fact]
    public async Task ReleasingTwiceDoesNotHandOutTheLockTwice()
    {
        using var locks = new SubscriberLockProvider();

        var release = await locks.AcquireAsync("demouser@microsoft.com");
        release.Dispose();
        release.Dispose();

        using var reacquired = await locks.AcquireAsync("demouser@microsoft.com");
        var contender = locks.AcquireAsync("demouser@microsoft.com");

        Assert.False(contender.IsCompleted);
        reacquired.Dispose();
        (await contender.WaitAsync(TimeSpan.FromSeconds(5))).Dispose();
    }
}
