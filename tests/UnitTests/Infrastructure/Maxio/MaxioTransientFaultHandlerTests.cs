using System.Net;
using System.Net.Http;
using System.Text;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Maxio;

public class MaxioTransientFaultHandlerTests
{
    [Fact]
    public async Task RateLimitedRequestsAreRetried()
    {
        var inner = new StubHttpMessageHandler();
        inner.Enqueue(HttpStatusCode.TooManyRequests, string.Empty);
        inner.Enqueue(HttpStatusCode.OK, """{"site":{"id":1}}""");

        var response = await SendAsync(inner, Post("first"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, inner.Requests.Count);
    }

    [Fact]
    public async Task ARetriedRequestResendsItsBody()
    {
        // A retry rebuilds the message, and a body that silently vanished on the second attempt
        // would turn a rate limit into a mysterious validation failure.
        var inner = new StubHttpMessageHandler();
        inner.Enqueue(HttpStatusCode.ServiceUnavailable, string.Empty);
        inner.Enqueue(HttpStatusCode.OK, "{}");

        await SendAsync(inner, Post("""{"subscription":{"reference":"eshop-demouser-eshop-pro"}}"""));

        Assert.Equal(2, inner.Requests.Count);
        Assert.All(inner.Requests, r => Assert.Equal("""{"subscription":{"reference":"eshop-demouser-eshop-pro"}}""", r.Body));
        Assert.All(inner.Requests, r => Assert.Equal(HttpMethod.Post, r.Method));
    }

    [Fact]
    public async Task ClientErrorsAreNotRetried()
    {
        var inner = new StubHttpMessageHandler();
        inner.Enqueue(HttpStatusCode.UnprocessableEntity, """{"errors":["Reference: must be unique - that value has been taken."]}""");

        var response = await SendAsync(inner, Post("body"));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Single(inner.Requests);
    }

    [Fact]
    public async Task RetriesStopAtTheConfiguredAttemptLimit()
    {
        var inner = new StubHttpMessageHandler();
        for (var i = 0; i < 5; i++)
        {
            inner.Enqueue(HttpStatusCode.BadGateway, string.Empty);
        }

        var response = await SendAsync(inner, Post("body"), maxAttempts: 3);

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        Assert.Equal(3, inner.Requests.Count);
    }

    [Fact]
    public async Task RetryingIsDisabledWhenOnlyOneAttemptIsAllowed()
    {
        var inner = new StubHttpMessageHandler();
        inner.Enqueue(HttpStatusCode.TooManyRequests, string.Empty);

        var response = await SendAsync(inner, Post("body"), maxAttempts: 1);

        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
        Assert.Single(inner.Requests);
    }

    [Fact]
    public async Task CallerCancellationIsNotTreatedAsATransientFault()
    {
        var inner = new StubHttpMessageHandler();
        using var cts = new CancellationTokenSource();
        inner.Enqueue(_ =>
        {
            cts.Cancel();
            throw new TaskCanceledException();
        });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => SendAsync(inner, Post("body"), cancellationToken: cts.Token));

        Assert.Single(inner.Requests);
    }

    private static HttpRequestMessage Post(string body) =>
        new(HttpMethod.Post, "https://acme.chargify.com/subscriptions.json")
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };

    private static async Task<HttpResponseMessage> SendAsync(
        StubHttpMessageHandler inner,
        HttpRequestMessage request,
        int maxAttempts = 3,
        CancellationToken cancellationToken = default)
    {
        var handler = new MaxioTransientFaultHandler(
            new StaticOptionsMonitor<MaxioSettings>(new MaxioSettings { MaxAttempts = maxAttempts }),
            NullLogger<MaxioTransientFaultHandler>.Instance)
        {
            InnerHandler = inner
        };

        using var client = new HttpClient(handler);
        return await client.SendAsync(request, cancellationToken);
    }
}
