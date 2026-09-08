using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Maxio;

/// <summary>
/// Tests for the per-(customer, plan) subscription gate. The gate serializes subscribe attempts
/// so a double-click can never create two subscriptions; it must release queued waiters and stay
/// usable across bursts (no deadlock, no dispose-while-waiting race).
/// </summary>
public class SubscriptionGateTests
{
    [Fact]
    public async Task WaiterActuallyAcquiresAfterRelease()
    {
        var gate = new SubscriptionGate();
        var first = await gate.EnterAsync("k", CancellationToken.None);

        var secondWaiter = Task.Run(async () =>
        {
            using (await gate.EnterAsync("k", CancellationToken.None))
            {
                return true;
            }
        });

        await Task.Delay(200);
        Assert.False(secondWaiter.IsCompleted);

        first.Dispose();

        var completed = await secondWaiter.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(completed);
    }

    [Fact]
    public async Task GateRemainsUsableThroughSequentialAndConcurrentTraffic()
    {
        var gate = new SubscriptionGate();

        // Sequential acquisition/release must keep working.
        for (var i = 0; i < 5; i++)
        {
            using (await gate.EnterAsync("sequential", CancellationToken.None))
            {
            }
        }

        // A burst of concurrent waiters on one key must all get through (exercises the
        // refcount/removal path that used to deadlock when the lease was never released).
        var burst = Enumerable.Range(0, 20).Select(_ => Task.Run(async () =>
        {
            using (await gate.EnterAsync("burst", CancellationToken.None))
            {
                await Task.Delay(10);
            }
        })).ToArray();

        await Task.WhenAll(burst);
    }

    [Fact]
    public async Task CancelledWaiterDoesNotCorruptLaterAcquisitions()
    {
        var gate = new SubscriptionGate();
        var first = await gate.EnterAsync("cancel", CancellationToken.None);

        using var cts = new CancellationTokenSource();
        var waiting = gate.EnterAsync("cancel", cts.Token);
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiting);

        // The cancelled waiter must have dropped its reference; releasing the holder must leave
        // the gate free for a fresh acquisition (no stale lock, no disposed semaphore).
        first.Dispose();
        using (await gate.EnterAsync("cancel", CancellationToken.None))
        {
        }
    }
}
