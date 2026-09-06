using System.Net;
using Microsoft.eShopWeb.Infrastructure.Billing.Maxio;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Billing;

public class MaxioResilienceHandlerTests
{
    private static HttpClient Build(HttpMessageHandler inner, out MaxioOptions options)
    {
        options = new MaxioOptions
        {
            ApiKey = "test-key",
            Subdomain = "acme",
            ProductFamilyHandle = "eshop-subscribe",
            RetryBaseDelayMilliseconds = 1
        };

        var handler = new MaxioResilienceHandler(new MaxioRequestGate(Options.Create(options)),
            Options.Create(options), NullLogger<MaxioResilienceHandler>.Instance)
        {
            InnerHandler = inner
        };

        return new HttpClient(handler) { BaseAddress = new Uri("https://acme.chargify.com/") };
    }

    [Fact]
    public async Task RetriesAThrottledReadUntilItSucceeds()
    {
        var attempts = 0;
        var inner = new FakeMaxioHandler().Map("GET /site.json", _ => ++attempts < 3
            ? FakeMaxioHandler.Respond(HttpStatusCode.TooManyRequests, "{}")
            : FakeMaxioHandler.Respond(HttpStatusCode.OK, """{"site":{"id":1}}"""));

        var response = await Build(inner, out _).GetAsync("site.json");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(3, attempts);
    }

    [Fact]
    public async Task GivesUpAfterTheConfiguredNumberOfAttempts()
    {
        var attempts = 0;
        var inner = new FakeMaxioHandler().Map("GET /site.json", _ =>
        {
            attempts++;
            return FakeMaxioHandler.Respond(HttpStatusCode.ServiceUnavailable, "{}");
        });

        var client = Build(inner, out var options);

        var response = await client.GetAsync("site.json");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal(options.MaxRetryAttempts + 1, attempts);
    }

    [Fact]
    public async Task DoesNotReplayAWriteThatCarriesNoDuplicatePreventionToken()
    {
        var attempts = 0;
        var inner = new FakeMaxioHandler().Map("POST /subscriptions.json", _ =>
        {
            attempts++;
            return FakeMaxioHandler.Respond(HttpStatusCode.InternalServerError, "{}");
        });

        // A 500 can mean the subscription was created anyway, so an unguarded POST is sent once.
        var response = await Build(inner, out _)
            .PostAsync("subscriptions.json", new StringContent("{}"));

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal(1, attempts);
    }

    [Fact]
    public async Task ReplaysAWriteThatIsMarkedSafeToRetry()
    {
        var attempts = 0;
        var inner = new FakeMaxioHandler().Map("POST /subscriptions.json", _ => ++attempts < 2
            ? FakeMaxioHandler.Respond(HttpStatusCode.BadGateway, "{}")
            : FakeMaxioHandler.Respond(HttpStatusCode.Created, """{"subscription":{"id":1}}"""));

        var request = new HttpRequestMessage(HttpMethod.Post, "subscriptions.json")
        {
            // The same content type MaxioApiClient sends, so the replay exercises re-serialization.
            Content = System.Net.Http.Json.JsonContent.Create(new { subscription = new { product_handle = "eshop-pro" } })
        };
        // The same option key MaxioApiClient sets on every request that carries a uniqueness token.
        request.Options.Set(new HttpRequestOptionsKey<bool>("Maxio.SafeToRetry"), true);

        var response = await Build(inner, out _).SendAsync(request);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(2, attempts);

        // Both attempts must carry the identical body, or the replay would not be a replay.
        Assert.Equal(2, inner.RequestBodies.Count);
        Assert.Equal(inner.RequestBodies[0], inner.RequestBodies[1]);
        Assert.Contains("eshop-pro", inner.RequestBodies[1]);
    }

    [Fact]
    public async Task HonoursTheRetryAfterHeader()
    {
        var attempts = 0;
        var inner = new FakeMaxioHandler().Map("GET /site.json", _ =>
        {
            if (++attempts >= 2)
            {
                return FakeMaxioHandler.Respond(HttpStatusCode.OK, """{"site":{"id":1}}""");
            }

            var throttled = FakeMaxioHandler.Respond(HttpStatusCode.TooManyRequests, "{}");
            throttled.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(
                TimeSpan.FromMilliseconds(50));
            return throttled;
        });

        var started = DateTimeOffset.UtcNow;
        var response = await Build(inner, out _).GetAsync("site.json");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(DateTimeOffset.UtcNow - started >= TimeSpan.FromMilliseconds(40));
    }
}
