using System;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Maxio;

public class MaxioTransientFaultHandlerTests
{
    private readonly StubHttpMessageHandler _inner = new();

    private HttpClient CreateClient(int retryAttempts = 3)
    {
        var settings = new MaxioSettings
        {
            ApiKey = "key",
            Subdomain = "acme",
            MaxRetryAttempts = retryAttempts,
            RetryBaseDelayMilliseconds = 10
        };

        var handler = new MaxioTransientFaultHandler(
            new StaticOptionsMonitor<MaxioSettings>(settings),
            NullLogger<MaxioTransientFaultHandler>.Instance)
        {
            InnerHandler = _inner
        };

        return new HttpClient(handler) { BaseAddress = new Uri("https://acme.chargify.com/") };
    }

    [Fact]
    public async Task RetriesReadsOnServerErrors()
    {
        _inner.Respond(HttpStatusCode.InternalServerError)
              .Respond(HttpStatusCode.BadGateway)
              .Respond(HttpStatusCode.OK, "[]");

        var response = await CreateClient().GetAsync("products.json");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(3, _inner.Requests.Count);
    }

    [Fact]
    public async Task RetriesReadsOnNetworkFailures()
    {
        _inner.RespondWith(_ => throw new HttpRequestException("connection reset"))
              .Respond(HttpStatusCode.OK, "[]");

        var response = await CreateClient().GetAsync("products.json");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, _inner.Requests.Count);
    }

    [Fact]
    public async Task DoesNotReplayWritesOnServerErrors()
    {
        _inner.Respond(HttpStatusCode.InternalServerError);

        var response = await CreateClient().PostAsync("subscriptions.json", new StringContent("{}"));

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Single(_inner.Requests);
    }

    [Fact]
    public async Task RetriesWritesOnRateLimiting()
    {
        _inner.Respond(HttpStatusCode.TooManyRequests)
              .Respond(HttpStatusCode.Created, """{"subscription":{"id":1}}""");

        var response = await CreateClient().PostAsync("subscriptions.json", new StringContent("{}"));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(2, _inner.Requests.Count);
    }

    [Fact]
    public async Task GivesUpAfterTheConfiguredNumberOfAttempts()
    {
        _inner.Respond(HttpStatusCode.InternalServerError)
              .Respond(HttpStatusCode.InternalServerError);

        var response = await CreateClient(retryAttempts: 1).GetAsync("products.json");

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal(2, _inner.Requests.Count);
    }

    [Fact]
    public async Task DoesNotRetrySuccessfulOrClientErrorResponses()
    {
        _inner.Respond(HttpStatusCode.UnprocessableEntity, """{"errors":["nope"]}""");

        var response = await CreateClient().GetAsync("products.json");

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Single(_inner.Requests);
    }
}
