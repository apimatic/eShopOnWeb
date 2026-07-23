using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.Infrastructure.Services;
using Xunit;

namespace Microsoft.eShopWeb.MaxioIntegrationTests.Client;

/// <summary>
/// The catalog cache: one resolution per lifetime window, expiry so provider-side changes are picked
/// up without a restart, and no caller-triggered re-resolution.
/// </summary>
public class MaxioCatalogCacheTests
{
    private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(5);

    [Fact]
    public async Task ResolvesOnceAndServesEverySubsequentCallFromTheCache()
    {
        var clock = new TestClock();
        using var cache = new MaxioCatalogCache(clock, Lifetime);
        var resolutions = 0;

        for (var i = 0; i < 10; i++)
        {
            await cache.GetAsync(_ => Resolve(ref resolutions), CancellationToken.None);
        }

        Assert.Equal(1, resolutions);
    }

    [Fact]
    public async Task StillServesFromTheCacheJustBeforeTheLifetimeElapses()
    {
        var clock = new TestClock();
        using var cache = new MaxioCatalogCache(clock, Lifetime);
        var resolutions = 0;

        await cache.GetAsync(_ => Resolve(ref resolutions), CancellationToken.None);
        clock.Advance(Lifetime - TimeSpan.FromSeconds(1));
        await cache.GetAsync(_ => Resolve(ref resolutions), CancellationToken.None);

        Assert.Equal(1, resolutions);
    }

    [Fact]
    public async Task ResolvesAgainOnceTheLifetimeHasElapsedSoAReSeedIsPickedUp()
    {
        var clock = new TestClock();
        using var cache = new MaxioCatalogCache(clock, Lifetime);
        var resolutions = 0;

        var first = await cache.GetAsync(_ => Resolve(ref resolutions), CancellationToken.None);
        clock.Advance(Lifetime);
        var second = await cache.GetAsync(_ => Resolve(ref resolutions), CancellationToken.None);

        Assert.Equal(2, resolutions);
        Assert.NotSame(first, second);
    }

    [Fact]
    public async Task DoesNotCacheAFailedResolutionSoTheNextCallRetries()
    {
        var clock = new TestClock();
        using var cache = new MaxioCatalogCache(clock, Lifetime);
        var attempts = 0;

        await Assert.ThrowsAsync<InvalidOperationException>(() => cache.GetAsync(
            _ =>
            {
                attempts++;
                throw new InvalidOperationException("provider down");
            },
            CancellationToken.None));

        var resolutions = 0;
        await cache.GetAsync(_ => Resolve(ref resolutions), CancellationToken.None);

        Assert.Equal(1, attempts);
        Assert.Equal(1, resolutions);
    }

    [Fact]
    public async Task CollapsesAConcurrentFirstUseBurstIntoASingleResolution()
    {
        var clock = new TestClock();
        using var cache = new MaxioCatalogCache(clock, Lifetime);
        var resolutions = 0;
        var release = new TaskCompletionSource();

        var callers = Enumerable.Range(0, 20)
            .Select(_ => cache.GetAsync(async _ =>
            {
                Interlocked.Increment(ref resolutions);
                await release.Task;

                return Catalog();
            }, CancellationToken.None))
            .ToArray();

        release.SetResult();
        await Task.WhenAll(callers);

        Assert.Equal(1, resolutions);
    }

    [Fact]
    public void RejectsANonPositiveLifetime()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new MaxioCatalogCache(new TestClock(), TimeSpan.Zero));
        Assert.Throws<ArgumentOutOfRangeException>(() => new MaxioCatalogCache(new TestClock(), TimeSpan.FromSeconds(-1)));
    }

    private static Task<MaxioCatalog> Resolve(ref int counter)
    {
        counter++;

        return Task.FromResult(Catalog());
    }

    private static MaxioCatalog Catalog() => new(
        3_026_729,
        "eshop-subscribe",
        new[] { new SubscriptionPlan(1, "eshop-pro", "Pro Plan", null, 29_900, 1, "month", false, "eshop-subscribe") },
        meteredComponent: null);

    private sealed class TestClock : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 7, 23, 12, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan amount) => _now = _now.Add(amount);
    }
}
