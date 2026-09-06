using Microsoft.eShopWeb.Infrastructure.Billing.Maxio;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Billing.Maxio;

public class SubscriberGateTests
{
    [Fact]
    public async Task LetsOnlyOneRequestPerShopperThrough()
    {
        using var gate = new SubscriberGate();

        using var held = await gate.AcquireAsync("shopper@example.com", CancellationToken.None);

        var second = gate.AcquireAsync("shopper@example.com", CancellationToken.None);
        var raced = await Task.WhenAny(second, Task.Delay(TimeSpan.FromMilliseconds(200)));

        Assert.NotSame(second, raced);

        held.Dispose();
        (await second).Dispose();
    }

    [Fact]
    public async Task DoesNotBlockOneShopperBehindAnother()
    {
        using var gate = new SubscriberGate();

        using var held = await gate.AcquireAsync("a@example.com", CancellationToken.None);
        using var other = await gate.AcquireAsync("b@example.com", CancellationToken.None);

        Assert.NotNull(other);
    }

    [Fact]
    public async Task TreatsShopperKeysCaseInsensitively()
    {
        using var gate = new SubscriberGate();

        using var held = await gate.AcquireAsync("Shopper@Example.com", CancellationToken.None);

        var second = gate.AcquireAsync("shopper@example.com", CancellationToken.None);
        var raced = await Task.WhenAny(second, Task.Delay(TimeSpan.FromMilliseconds(200)));

        Assert.NotSame(second, raced);

        held.Dispose();
        (await second).Dispose();
    }

    [Fact]
    public async Task HandsTheGateToTheNextWaiterOnRelease()
    {
        using var gate = new SubscriberGate();

        var held = await gate.AcquireAsync("shopper@example.com", CancellationToken.None);
        var second = gate.AcquireAsync("shopper@example.com", CancellationToken.None);

        held.Dispose();

        using var acquired = await second.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.NotNull(acquired);
    }

    [Fact]
    public async Task SerialisesConcurrentRequestsForTheSameShopper()
    {
        using var gate = new SubscriberGate();

        var concurrent = 0;
        var peak = 0;

        await Task.WhenAll(Enumerable.Range(0, 16).Select(async _ =>
        {
            using var _held = await gate.AcquireAsync("shopper@example.com", CancellationToken.None);

            peak = Math.Max(peak, Interlocked.Increment(ref concurrent));
            await Task.Delay(5);
            Interlocked.Decrement(ref concurrent);
        }));

        Assert.Equal(1, peak);
    }

    [Fact]
    public async Task ReleasingIsIdempotent()
    {
        using var gate = new SubscriberGate();

        var held = await gate.AcquireAsync("shopper@example.com", CancellationToken.None);
        held.Dispose();
        held.Dispose();

        // A double release must not leave the gate permanently open.
        using var next = await gate.AcquireAsync("shopper@example.com", CancellationToken.None);
        var second = gate.AcquireAsync("shopper@example.com", CancellationToken.None);
        var raced = await Task.WhenAny(second, Task.Delay(TimeSpan.FromMilliseconds(200)));

        Assert.NotSame(second, raced);
    }

    [Fact]
    public async Task StopsWaitingWhenTheRequestIsCancelled()
    {
        using var gate = new SubscriberGate();
        using var cancellation = new CancellationTokenSource();

        using var held = await gate.AcquireAsync("shopper@example.com", CancellationToken.None);

        var second = gate.AcquireAsync("shopper@example.com", cancellation.Token);
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => second);
    }
}
