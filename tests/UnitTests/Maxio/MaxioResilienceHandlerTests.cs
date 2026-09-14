using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using Microsoft.eShopWeb.Infrastructure.Maxio.Contracts;
using Microsoft.eShopWeb.Infrastructure.Maxio.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Maxio;

public class MaxioResilienceHandlerTests
{
    private static readonly MaxioOptions Options = new()
    {
        ApiKey = "key",
        Subdomain = "acme",
        ProductFamilyHandle = "family",
        MaxRetryAttempts = 3,
        RetryBaseDelay = TimeSpan.FromMilliseconds(1)
    };

    private static MaxioApiClient CreateClient(StubHttpMessageHandler stub, MaxioOptions? options = null)
    {
        var handler = new MaxioResilienceHandler(
            new StaticMonitor(options ?? Options),
            NullLogger<MaxioResilienceHandler>.Instance)
        {
            InnerHandler = stub
        };

        return new MaxioApiClient(
            new HttpClient(handler) { BaseAddress = new Uri("https://acme.chargify.com/") },
            NullLogger<MaxioApiClient>.Instance);
    }

    [Fact]
    public async Task RetriesAServerFaultOnAReadUntilItSucceeds()
    {
        var stub = new StubHttpMessageHandler((_, call) => call < 3
            ? StubHttpMessageHandler.Json(HttpStatusCode.InternalServerError, "{}")
            : StubHttpMessageHandler.Json(HttpStatusCode.OK, """[{"product_family":{"id":1,"handle":"family"}}]"""));

        var families = await CreateClient(stub).ListProductFamiliesAsync();

        Assert.Single(families);
        Assert.Equal(3, stub.Requests.Count);
    }

    [Fact]
    public async Task RetriesATransportFaultOnARead()
    {
        var stub = new StubHttpMessageHandler((_, call) => call == 1
            ? throw new HttpRequestException("connection reset")
            : StubHttpMessageHandler.Json(HttpStatusCode.OK, "[]"));

        await CreateClient(stub).ListProductFamiliesAsync();

        Assert.Equal(2, stub.Requests.Count);
    }

    [Fact]
    public async Task GivesUpAfterTheConfiguredNumberOfAttempts()
    {
        var stub = new StubHttpMessageHandler((_, _) => StubHttpMessageHandler.Json(HttpStatusCode.BadGateway, "{}"));

        var exception = await Assert.ThrowsAsync<MaxioApiException>(() => CreateClient(stub).ListProductFamiliesAsync());

        Assert.Equal(HttpStatusCode.BadGateway, exception.StatusCode);
        Assert.Equal(Options.MaxRetryAttempts + 1, stub.Requests.Count);
    }

    [Fact]
    public async Task DoesNotRetryAServerFaultOnASubscriptionCreate()
    {
        // A 5xx on POST /subscriptions.json may already have enrolled the shopper; replaying it could
        // create a second subscription, so recovery is left to the caller's own idempotency check.
        var stub = new StubHttpMessageHandler((_, _) => StubHttpMessageHandler.Json(HttpStatusCode.InternalServerError, "{}"));

        await Assert.ThrowsAsync<MaxioApiException>(
            () => CreateClient(stub).CreateSubscriptionAsync(new CreateSubscription { ProductHandle = "eshop-pro" }));

        Assert.Single(stub.Requests);
    }

    [Fact]
    public async Task RetriesThrottlingEvenOnAWrite_AndResendsTheBody()
    {
        // 429 means the request was rejected before it was processed, so replaying it is safe.
        var stub = new StubHttpMessageHandler((_, call) => call == 1
            ? StubHttpMessageHandler.Json(HttpStatusCode.TooManyRequests, "{}")
            : StubHttpMessageHandler.Json(HttpStatusCode.Created, """{"subscription":{"id":900,"state":"active"}}"""));

        var subscription = await CreateClient(stub).CreateSubscriptionAsync(new CreateSubscription
        {
            ProductHandle = "eshop-pro",
            CustomerId = 42
        });

        Assert.Equal(900, subscription.Id);
        Assert.Equal(2, stub.Requests.Count);
        Assert.All(stub.RequestBodies, body => Assert.Contains("\"product_handle\":\"eshop-pro\"", body));
    }

    [Fact]
    public async Task HonoursARetryAfterHeader()
    {
        var stub = new StubHttpMessageHandler((_, call) =>
        {
            if (call > 1)
            {
                return StubHttpMessageHandler.Json(HttpStatusCode.OK, "[]");
            }

            var throttled = StubHttpMessageHandler.Json(HttpStatusCode.TooManyRequests, "{}");
            throttled.Headers.Add("Retry-After", "1");
            return throttled;
        });

        var stopwatch = Stopwatch.StartNew();
        await CreateClient(stub).ListProductFamiliesAsync();
        stopwatch.Stop();

        Assert.Equal(2, stub.Requests.Count);
        Assert.True(stopwatch.Elapsed >= TimeSpan.FromMilliseconds(900), $"waited only {stopwatch.Elapsed}");
    }

    [Fact]
    public async Task DoesNotRetryWhenRetriesAreDisabled()
    {
        var stub = new StubHttpMessageHandler((_, _) => StubHttpMessageHandler.Json(HttpStatusCode.BadGateway, "{}"));
        var options = new MaxioOptions
        {
            ApiKey = "key",
            Subdomain = "acme",
            ProductFamilyHandle = "family",
            MaxRetryAttempts = 0
        };

        await Assert.ThrowsAsync<MaxioApiException>(() => CreateClient(stub, options).ListProductFamiliesAsync());

        Assert.Single(stub.Requests);
    }

    private sealed class StaticMonitor : IOptionsMonitor<MaxioOptions>
    {
        public StaticMonitor(MaxioOptions options) => CurrentValue = options;

        public MaxioOptions CurrentValue { get; }

        public MaxioOptions Get(string? name) => CurrentValue;

        public IDisposable? OnChange(Action<MaxioOptions, string?> listener) => null;
    }
}
