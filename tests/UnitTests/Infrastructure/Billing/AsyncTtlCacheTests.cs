using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.Infrastructure.Billing;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Billing;

public class AsyncTtlCacheTests
{
    [Fact]
    public async Task ProducesTheValueOnceAndThenServesItFromCache()
    {
        var cache = new AsyncTtlCache<string>(TimeSpan.FromMinutes(1));
        var calls = 0;

        var first = await cache.GetAsync(_ => Task.FromResult((++calls).ToString()));
        var second = await cache.GetAsync(_ => Task.FromResult((++calls).ToString()));

        Assert.Equal("1", first);
        Assert.Equal("1", second);
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task CollapsesConcurrentMissesIntoASingleCall()
    {
        // Without single-flight, a cold cache under load fans out one upstream call per request.
        var cache = new AsyncTtlCache<string>(TimeSpan.FromMinutes(1));
        var calls = 0;

        var results = await Task.WhenAll(Enumerable.Range(0, 12).Select(_ => cache.GetAsync(async _ =>
        {
            Interlocked.Increment(ref calls);
            await Task.Delay(20);
            return "value";
        })));

        Assert.Equal(1, calls);
        Assert.All(results, r => Assert.Equal("value", r));
    }

    [Fact]
    public async Task RefetchesOnceTheEntryHasExpired()
    {
        var now = DateTimeOffset.UnixEpoch;
        var cache = new AsyncTtlCache<string>(TimeSpan.FromMinutes(5), () => now);
        var calls = 0;

        await cache.GetAsync(_ => Task.FromResult((++calls).ToString()));
        now = now.AddMinutes(6);
        var afterExpiry = await cache.GetAsync(_ => Task.FromResult((++calls).ToString()));

        Assert.Equal("2", afterExpiry);
    }

    [Fact]
    public async Task InvalidateForcesTheNextReadUpstream()
    {
        var cache = new AsyncTtlCache<string>(TimeSpan.FromMinutes(5));
        var calls = 0;

        await cache.GetAsync(_ => Task.FromResult((++calls).ToString()));
        cache.Invalidate();
        var refreshed = await cache.GetAsync(_ => Task.FromResult((++calls).ToString()));

        Assert.Equal("2", refreshed);
    }
}
