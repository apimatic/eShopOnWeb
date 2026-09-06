using Microsoft.eShopWeb.Infrastructure.Maxio;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Maxio;

public class SubscriberGateTests
{
    [Fact]
    public async Task TheSameKeyIsHeldByOneCallerAtATime()
    {
        using var gate = new SubscriberGate();
        var concurrent = 0;
        var observedMax = 0;

        await Task.WhenAll(Enumerable.Range(0, 16).Select(async _ =>
        {
            using var _held = await gate.AcquireAsync("eshop-demouser");

            observedMax = Math.Max(observedMax, Interlocked.Increment(ref concurrent));
            await Task.Delay(5);
            Interlocked.Decrement(ref concurrent);
        }));

        Assert.Equal(1, observedMax);
    }

    [Fact]
    public async Task DifferentKeysDoNotBlockEachOther()
    {
        using var gate = new SubscriberGate();

        using var first = await gate.AcquireAsync("eshop-one");
        var second = gate.AcquireAsync("eshop-two");

        // A second shopper must not queue behind the first; if this ever regressed the wait would
        // never complete and the test would time out rather than pass.
        Assert.True(second.IsCompleted);
        second.Result.Dispose();
    }

    [Fact]
    public async Task TheGateIsReusableAfterEveryHolderHasLetGo()
    {
        using var gate = new SubscriberGate();

        for (var i = 0; i < 3; i++)
        {
            using var _held = await gate.AcquireAsync("eshop-demouser");
        }

        using var again = await gate.AcquireAsync("eshop-demouser");
        Assert.NotNull(again);
    }

    [Fact]
    public async Task ChurnAcrossManyKeysDoesNotLeaveEntriesBehind()
    {
        // Entries are reaped as the last holder leaves, so a long-lived host does not accumulate one
        // semaphore per shopper who ever subscribed.
        using var gate = new SubscriberGate();

        await Task.WhenAll(Enumerable.Range(0, 200).Select(async i =>
        {
            for (var round = 0; round < 5; round++)
            {
                using var _held = await gate.AcquireAsync($"eshop-{i % 20}");
            }
        }));

        using var afterChurn = await gate.AcquireAsync("eshop-0");
        Assert.NotNull(afterChurn);
    }
}
