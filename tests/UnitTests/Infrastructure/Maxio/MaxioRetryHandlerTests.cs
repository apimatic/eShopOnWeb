using System;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Maxio;

public class MaxioRetryHandlerTests
{
    private static (HttpClient Client, StubHttpMessageHandler Inner) CreateClient(int maxRetryAttempts = 3)
    {
        var inner = new StubHttpMessageHandler();
        var options = Options.Create(new MaxioOptions
        {
            MaxRetryAttempts = maxRetryAttempts,
            RetryBaseDelayMilliseconds = 1
        });

        var retry = new MaxioRetryHandler(options, NullLogger<MaxioRetryHandler>.Instance)
        {
            InnerHandler = inner
        };

        return (new HttpClient(retry), inner);
    }

    [Fact]
    public async Task RetriesAFailedRead()
    {
        var (client, inner) = CreateClient();
        inner.Enqueue(HttpStatusCode.InternalServerError)
             .Enqueue(HttpStatusCode.OK, "{}");

        var response = await client.GetAsync("https://acme.chargify.com/site.json");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, inner.Requests.Count);
    }

    [Fact]
    public async Task GivesUpAfterTheConfiguredNumberOfAttempts()
    {
        var (client, inner) = CreateClient(maxRetryAttempts: 2);
        inner.Enqueue(HttpStatusCode.ServiceUnavailable)
             .Enqueue(HttpStatusCode.ServiceUnavailable)
             .Enqueue(HttpStatusCode.ServiceUnavailable);

        var response = await client.GetAsync("https://acme.chargify.com/site.json");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal(3, inner.Requests.Count);
    }

    [Fact]
    public async Task DoesNotRetryDisabledByConfiguration()
    {
        var (client, inner) = CreateClient(maxRetryAttempts: 0);
        inner.Enqueue(HttpStatusCode.InternalServerError);

        await client.GetAsync("https://acme.chargify.com/site.json");

        Assert.Single(inner.Requests);
    }

    [Fact]
    public async Task NeverReplaysAWriteThatMayHaveBeenProcessed()
    {
        var (client, inner) = CreateClient();
        inner.Enqueue(HttpStatusCode.InternalServerError);

        var response = await client.PostAsync(
            "https://acme.chargify.com/subscriptions.json", new StringContent("{}"));

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Single(inner.Requests);
    }

    [Fact]
    public async Task ReplaysAWriteTheProviderRejectedOutright()
    {
        var (client, inner) = CreateClient();
        inner.Enqueue(HttpStatusCode.TooManyRequests)
             .Enqueue(HttpStatusCode.Created, "{}");

        var response = await client.PostAsync(
            "https://acme.chargify.com/subscriptions.json", new StringContent("{}"));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(2, inner.Requests.Count);
    }

    [Fact]
    public async Task ReplaysAWriteAGatewayNeverForwarded()
    {
        var (client, inner) = CreateClient();
        inner.Enqueue(HttpStatusCode.BadGateway)
             .Enqueue(HttpStatusCode.Created, "{}");

        var response = await client.PostAsync(
            "https://acme.chargify.com/subscriptions.json", new StringContent("{}"));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(2, inner.Requests.Count);
    }

    [Fact]
    public async Task HonoursRetryAfter()
    {
        var (client, inner) = CreateClient();
        var throttled = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
        throttled.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromMilliseconds(200));
        inner.EnqueueResponse(throttled).Enqueue(HttpStatusCode.OK, "{}");

        var started = DateTimeOffset.UtcNow;
        var response = await client.GetAsync("https://acme.chargify.com/site.json");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(DateTimeOffset.UtcNow - started >= TimeSpan.FromMilliseconds(150));
    }

    [Fact]
    public async Task DoesNotRetryAClientError()
    {
        var (client, inner) = CreateClient();
        inner.Enqueue(HttpStatusCode.UnprocessableEntity, """{"errors":["nope"]}""");

        var response = await client.GetAsync("https://acme.chargify.com/site.json");

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Single(inner.Requests);
    }
}
