using System.Net;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using Microsoft.eShopWeb.Infrastructure.Maxio.Http;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Maxio;

public class MaxioRetryHandlerTests
{
    [Fact]
    public async Task RetriesAServerErrorOnASafeMethod()
    {
        var inner = new StubHttpMessageHandler()
            .Respond(HttpStatusCode.ServiceUnavailable)
            .Respond(HttpStatusCode.OK, "[]");

        var response = await SendAsync(inner, HttpMethod.Get);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, inner.Requests.Count);
    }

    [Fact]
    public async Task DoesNotReplayAPostAfterAServerError()
    {
        // Replaying a create could enroll the shopper twice; the caller's idempotency check resolves it.
        var inner = new StubHttpMessageHandler().Respond(HttpStatusCode.InternalServerError);

        var response = await SendAsync(inner, HttpMethod.Post);

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Single(inner.Requests);
    }

    [Fact]
    public async Task RetriesARateLimitedPostBecauseItWasNeverProcessed()
    {
        var inner = new StubHttpMessageHandler()
            .Respond(HttpStatusCode.TooManyRequests)
            .Respond(HttpStatusCode.Created, "{}");

        var response = await SendAsync(inner, HttpMethod.Post);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(2, inner.Requests.Count);
    }

    [Fact]
    public async Task ReSendsTheRequestBodyWhenItRetries()
    {
        var inner = new StubHttpMessageHandler()
            .Respond(HttpStatusCode.TooManyRequests)
            .Respond(HttpStatusCode.Created, "{}");

        var settings = MaxioTestFactory.Settings(s =>
        {
            s.MaxRetryAttempts = 3;
            s.RetryBaseDelayMilliseconds = 1;
        });

        var retryHandler = new MaxioRetryHandler(
            new StaticOptionsMonitor<MaxioSettings>(settings),
            NullLoggerFactory.CreateLogger<MaxioRetryHandler>())
        {
            InnerHandler = inner
        };

        using var client = new HttpClient(retryHandler) { BaseAddress = new Uri("https://acme.chargify.com/") };
        var request = new HttpRequestMessage(HttpMethod.Post, "subscriptions.json")
        {
            Content = System.Net.Http.Json.JsonContent.Create(
                new { subscription = new { product_handle = "pro-plan" } })
        };

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(2, inner.Requests.Count);
        Assert.All(inner.Requests, recorded => Assert.Contains("pro-plan", recorded.Body));
    }

    [Fact]
    public async Task HonoursRetryAfterInsteadOfTheComputedBackoff()
    {
        var inner = new StubHttpMessageHandler()
            .Respond(_ =>
            {
                var throttled = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
                throttled.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromSeconds(1));
                return throttled;
            })
            .Respond(HttpStatusCode.OK, "[]");

        var startedAt = DateTimeOffset.UtcNow;
        var response = await SendAsync(inner, HttpMethod.Get);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(DateTimeOffset.UtcNow - startedAt >= TimeSpan.FromMilliseconds(900));
    }

    [Fact]
    public async Task GivesUpAfterTheConfiguredNumberOfAttempts()
    {
        var inner = new StubHttpMessageHandler()
            .Respond(HttpStatusCode.BadGateway)
            .Respond(HttpStatusCode.BadGateway)
            .Respond(HttpStatusCode.BadGateway);

        var response = await SendAsync(inner, HttpMethod.Get, maxRetries: 2);

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        Assert.Equal(3, inner.Requests.Count);
    }

    [Fact]
    public async Task DoesNotRetryABadRequest()
    {
        var inner = new StubHttpMessageHandler().Respond(HttpStatusCode.UnprocessableEntity, """{"errors":["nope"]}""");

        var response = await SendAsync(inner, HttpMethod.Get);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Single(inner.Requests);
    }

    private static async Task<HttpResponseMessage> SendAsync(
        StubHttpMessageHandler inner,
        HttpMethod method,
        int maxRetries = 3)
    {
        var settings = MaxioTestFactory.Settings(s =>
        {
            s.MaxRetryAttempts = maxRetries;
            s.RetryBaseDelayMilliseconds = 1;
        });

        var retryHandler = new MaxioRetryHandler(
            new StaticOptionsMonitor<MaxioSettings>(settings),
            NullLoggerFactory.CreateLogger<MaxioRetryHandler>())
        {
            InnerHandler = inner
        };

        using var client = new HttpClient(retryHandler) { BaseAddress = new Uri("https://acme.chargify.com/") };
        return await client.SendAsync(new HttpRequestMessage(method, "customers.json"));
    }
}
