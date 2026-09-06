using System;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.MaxioBilling;

public class MaxioRetryHandlerTests
{
    private readonly StubHttpMessageHandler _inner = new();

    [Fact]
    public async Task RetriesAFailedReadAndReturnsTheEventualSuccess()
    {
        _inner.Respond(HttpStatusCode.InternalServerError)
              .Respond(HttpStatusCode.BadGateway)
              .Respond(HttpStatusCode.OK, "{}");

        var response = await Send(HttpMethod.Get);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(3, _inner.Requests.Count);
    }

    [Fact]
    public async Task GivesUpOnAReadAfterTheConfiguredNumberOfAttempts()
    {
        for (var i = 0; i < 5; i++)
        {
            _inner.Respond(HttpStatusCode.InternalServerError);
        }

        var response = await Send(HttpMethod.Get);

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal(3, _inner.Requests.Count); // 1 attempt + 2 retries
    }

    [Fact]
    public async Task NeverRetriesAWriteThatMayHaveBeenApplied()
    {
        _inner.Respond(HttpStatusCode.InternalServerError).Respond(HttpStatusCode.OK, "{}");

        var response = await Send(HttpMethod.Post);

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Single(_inner.Requests);
    }

    [Fact]
    public async Task RetriesAThrottledWriteBecauseItWasNeverProcessed()
    {
        _inner.Respond(HttpStatusCode.TooManyRequests).Respond(HttpStatusCode.Created, "{}");

        var response = await Send(HttpMethod.Post);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(2, _inner.Requests.Count);
    }

    [Fact]
    public async Task ResendsTheSameBodyOnARetriedWrite()
    {
        _inner.Respond(HttpStatusCode.TooManyRequests).Respond(HttpStatusCode.Created, "{}");

        await Send(HttpMethod.Post, """{"subscription":{"product_handle":"eshop-pro"}}""");

        Assert.Equal(2, _inner.Requests.Count);
        Assert.Equal(_inner.Requests[0].Body, _inner.Requests[1].Body);
    }

    private async Task<HttpResponseMessage> Send(HttpMethod method, string? body = null)
    {
        var options = new MaxioOptions { MaxRetryAttempts = 2, RetryBaseDelayMilliseconds = 1 };
        var retryHandler = new MaxioRetryHandler(new StaticOptionsMonitor(options), NullLogger<MaxioRetryHandler>.Instance)
        {
            InnerHandler = _inner
        };

        using var client = new HttpClient(retryHandler) { BaseAddress = new Uri("https://acme.chargify.com/") };
        var request = new HttpRequestMessage(method, "subscriptions.json");
        if (body is not null)
        {
            request.Content = new ByteArrayContent(System.Text.Encoding.UTF8.GetBytes(body));
        }

        return await client.SendAsync(request);
    }

    private sealed class StaticOptionsMonitor : IOptionsMonitor<MaxioOptions>
    {
        public StaticOptionsMonitor(MaxioOptions value) => CurrentValue = value;

        public MaxioOptions CurrentValue { get; }

        public MaxioOptions Get(string? name) => CurrentValue;

        public IDisposable? OnChange(Action<MaxioOptions, string?> listener) => null;
    }
}
