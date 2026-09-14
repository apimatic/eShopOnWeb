using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Maxio;

public class MaxioRetryHandlerTests
{
    private readonly StubHttpMessageHandler _inner = new();

    private HttpClient CreateClient(int maxAttempts = 3)
    {
        var monitor = Substitute.For<IOptionsMonitor<MaxioSettings>>();
        monitor.CurrentValue.Returns(new MaxioSettings { MaxRetryAttempts = maxAttempts });

        var retryHandler = new MaxioRetryHandler(monitor, NullLogger<MaxioRetryHandler>.Instance)
        {
            InnerHandler = _inner
        };

        return new HttpClient(retryHandler) { BaseAddress = new Uri("https://acme.chargify.com/") };
    }

    [Fact]
    public async Task RetriesRateLimitedRequestsEvenWhenTheyAreNotIdempotent()
    {
        // A 429 means Maxio rejected the request outright, so replaying a POST cannot double-charge.
        _inner.Respond(HttpStatusCode.TooManyRequests).Respond(HttpStatusCode.Created, "{}");

        var response = await CreateClient().PostAsync("subscriptions.json", new StringContent("{}"));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(2, _inner.Requests.Count);
    }

    [Fact]
    public async Task RetriesServerErrorsForReads()
    {
        _inner.Respond(HttpStatusCode.BadGateway).Respond(HttpStatusCode.OK, "{}");

        var response = await CreateClient().GetAsync("site.json");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, _inner.Requests.Count);
    }

    [Fact]
    public async Task DoesNotReplayAWriteThatMayHaveBeenProcessed()
    {
        // Maxio could have created the subscription before the 500; retrying risks a duplicate.
        _inner.Respond(HttpStatusCode.InternalServerError);

        var response = await CreateClient().PostAsync("subscriptions.json", new StringContent("{}"));

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Single(_inner.Requests);
    }

    [Fact]
    public async Task DoesNotReplayATransportFaultOnAWrite()
    {
        _inner.Throw(new HttpRequestException("connection reset"));

        await Assert.ThrowsAsync<HttpRequestException>(
            () => CreateClient().PostAsync("subscriptions.json", new StringContent("{}")));

        Assert.Single(_inner.Requests);
    }

    [Fact]
    public async Task RetriesATransportFaultOnARead()
    {
        _inner.Throw(new HttpRequestException("connection reset")).Respond(HttpStatusCode.OK, "{}");

        var response = await CreateClient().GetAsync("site.json");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, _inner.Requests.Count);
    }

    [Fact]
    public async Task GivesUpAfterTheConfiguredNumberOfAttempts()
    {
        _inner.Respond(HttpStatusCode.TooManyRequests)
              .Respond(HttpStatusCode.TooManyRequests)
              .Respond(HttpStatusCode.TooManyRequests);

        var response = await CreateClient(maxAttempts: 3).GetAsync("site.json");

        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
        Assert.Equal(3, _inner.Requests.Count);
    }

    [Fact]
    public async Task DoesNotRetryASuccessOrAClientError()
    {
        _inner.Respond(HttpStatusCode.NotFound);

        var response = await CreateClient().GetAsync("customers/lookup.json?reference=nobody");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Single(_inner.Requests);
    }

    [Fact]
    public async Task HonoursRetryAfterWhenMaxioSendsIt()
    {
        _inner.RespondWith(_ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
            response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromMilliseconds(400));
            return response;
        }).Respond(HttpStatusCode.OK, "{}");

        var started = DateTimeOffset.UtcNow;
        var result = await CreateClient().GetAsync("site.json");

        Assert.Equal(HttpStatusCode.OK, result.StatusCode);
        Assert.True(DateTimeOffset.UtcNow - started >= TimeSpan.FromMilliseconds(300));
    }

    [Fact]
    public async Task StopsRetryingWhenTheCallerCancels()
    {
        _inner.Respond(HttpStatusCode.TooManyRequests).Respond(HttpStatusCode.OK, "{}");
        using var cancellation = new CancellationTokenSource();

        var request = CreateClient().GetAsync("site.json", cancellation.Token);
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => request);
    }
}
