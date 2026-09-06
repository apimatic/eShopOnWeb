using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using Microsoft.eShopWeb.Infrastructure.Maxio.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Maxio;

public class MaxioAuthenticationHandlerSendAsync
{
    [Fact]
    public async Task AppliesTheApiKeyAsBasicAuthWithThePasswordTheSpecificationDefines()
    {
        var options = MaxioTestOptions.Valid();
        options.ApiKey = "abc123";

        var inner = new StubHttpMessageHandler().Enqueue(HttpStatusCode.OK, "{}");
        var handler = new MaxioAuthenticationHandler(new StaticOptionsMonitor<MaxioOptions>(options)) { InnerHandler = inner };

        using var client = new HttpClient(handler);
        await client.GetAsync("https://acme.chargify.com/site.json");

        var authorization = inner.Requests[0].Headers.Authorization;
        Assert.Equal("Basic", authorization!.Scheme);
        Assert.Equal("abc123:x", Encoding.UTF8.GetString(Convert.FromBase64String(authorization.Parameter!)));
    }
}

public class MaxioResilienceHandlerSendAsync
{
    private static (HttpClient Client, StubHttpMessageHandler Inner) Build(int maxRetryAttempts)
    {
        var options = MaxioTestOptions.Valid();
        options.MaxRetryAttempts = maxRetryAttempts;
        options.RetryBaseDelay = TimeSpan.FromMilliseconds(1);

        var inner = new StubHttpMessageHandler();
        var handler = new MaxioResilienceHandler(
            new StaticOptionsMonitor<MaxioOptions>(options),
            NullLogger<MaxioResilienceHandler>.Instance)
        {
            InnerHandler = inner
        };

        return (new HttpClient(handler), inner);
    }

    [Fact]
    public async Task RetriesAFailedReadAndReturnsTheEventualSuccess()
    {
        var (client, inner) = Build(maxRetryAttempts: 2);
        inner.Enqueue(HttpStatusCode.ServiceUnavailable).Enqueue(HttpStatusCode.OK, "{}");

        var response = await client.GetAsync("https://acme.chargify.com/site.json");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, inner.Requests.Count);
    }

    [Fact]
    public async Task GivesUpOnceTheRetryBudgetIsSpent()
    {
        var (client, inner) = Build(maxRetryAttempts: 2);
        inner.Enqueue(HttpStatusCode.BadGateway).Enqueue(HttpStatusCode.BadGateway).Enqueue(HttpStatusCode.BadGateway);

        var response = await client.GetAsync("https://acme.chargify.com/site.json");

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        Assert.Equal(3, inner.Requests.Count);
    }

    [Fact]
    public async Task DoesNotRepeatAWriteAfterAServerErrorBecauseItMayHaveBeenApplied()
    {
        var (client, inner) = Build(maxRetryAttempts: 3);
        inner.Enqueue(HttpStatusCode.InternalServerError);

        var response = await client.PostAsync("https://acme.chargify.com/subscriptions.json", new StringContent("{}"));

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Single(inner.Requests);
    }

    [Fact]
    public async Task RepeatsAWriteThatWasRateLimited()
    {
        var (client, inner) = Build(maxRetryAttempts: 3);
        inner.Enqueue(HttpStatusCode.TooManyRequests).Enqueue(HttpStatusCode.Created, "{}");

        var response = await client.PostAsync("https://acme.chargify.com/subscriptions.json", new StringContent("{}"));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(2, inner.Requests.Count);
    }

    [Fact]
    public async Task DoesNotRepeatAWriteAfterATransportFailure()
    {
        var (client, inner) = Build(maxRetryAttempts: 3);
        inner.EnqueueThrow(new HttpRequestException("connection reset"));

        await Assert.ThrowsAsync<HttpRequestException>(
            () => client.PostAsync("https://acme.chargify.com/subscriptions.json", new StringContent("{}")));

        Assert.Single(inner.Requests);
    }

}

public class MaxioResilienceHandlerRetryAfterHeader
{
    [Fact]
    public async Task WaitsForTheDurationTheProviderAsksFor()
    {
        var options = MaxioTestOptions.Valid();
        options.MaxRetryAttempts = 1;
        // Near-zero back-off, so any measurable wait can only have come from Retry-After.
        options.RetryBaseDelay = TimeSpan.FromMilliseconds(1);

        var inner = new RetryAfterStub(TimeSpan.FromSeconds(1));
        var handler = new MaxioResilienceHandler(
            new StaticOptionsMonitor<MaxioOptions>(options),
            NullLogger<MaxioResilienceHandler>.Instance)
        {
            InnerHandler = inner
        };

        using var client = new HttpClient(handler);

        var started = DateTimeOffset.UtcNow;
        var response = await client.GetAsync("https://acme.chargify.com/site.json");
        var elapsed = DateTimeOffset.UtcNow - started;

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.InRange(elapsed, TimeSpan.FromMilliseconds(800), TimeSpan.FromSeconds(10));
    }

    private sealed class RetryAfterStub : HttpMessageHandler
    {
        private readonly TimeSpan _retryAfter;
        private int _calls;

        public RetryAfterStub(TimeSpan retryAfter) => _retryAfter = retryAfter;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (_calls++ == 0)
            {
                var throttled = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
                throttled.Headers.RetryAfter = new RetryConditionHeaderValue(_retryAfter);
                return Task.FromResult(throttled);
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        }
    }
}
