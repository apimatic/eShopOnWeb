using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.Infrastructure.Billing.Maxio;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Billing.Maxio;

public class MaxioRetryHandlerTests
{
    [Fact]
    public async Task RetriesAFailedReadUntilItSucceeds()
    {
        var inner = new StubHandler(HttpStatusCode.ServiceUnavailable, HttpStatusCode.BadGateway, HttpStatusCode.OK);

        var response = await SendAsync(inner, HttpMethod.Get);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(3, inner.Attempts);
    }

    [Fact]
    public async Task GivesUpAfterTheConfiguredNumberOfAttempts()
    {
        var inner = new StubHandler(Enumerable(HttpStatusCode.ServiceUnavailable, 10));

        var response = await SendAsync(inner, HttpMethod.Get, maxRetryAttempts: 2);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal(3, inner.Attempts); // the initial call plus two retries
    }

    [Fact]
    public async Task NeverRetriesAWriteThatFailedServerSide()
    {
        // A 500 on POST /subscriptions.json may mean the subscription was created and only the response
        // was lost. Retrying it would enroll the shopper twice, so the failure is surfaced instead.
        var inner = new StubHandler(HttpStatusCode.InternalServerError, HttpStatusCode.OK);

        var response = await SendAsync(inner, HttpMethod.Post);

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal(1, inner.Attempts);
    }

    [Fact]
    public async Task RetriesAThrottledWriteBecauseItWasNeverProcessed()
    {
        var inner = new StubHandler(HttpStatusCode.TooManyRequests, HttpStatusCode.OK);

        var response = await SendAsync(inner, HttpMethod.Post);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, inner.Attempts);
    }

    [Fact]
    public async Task RetriesATransportFailureOnAReadButNotOnAWrite()
    {
        var read = new StubHandler(new HttpRequestException("connection reset"), HttpStatusCode.OK);
        var readResponse = await SendAsync(read, HttpMethod.Get);
        Assert.Equal(HttpStatusCode.OK, readResponse.StatusCode);

        var write = new StubHandler(new HttpRequestException("connection reset"), HttpStatusCode.OK);
        await Assert.ThrowsAsync<HttpRequestException>(() => SendAsync(write, HttpMethod.Post));
        Assert.Equal(1, write.Attempts);
    }

    [Fact]
    public async Task DoesNotRetryAValidationFailure()
    {
        // 422 is Maxio saying no, not Maxio being unavailable.
        var inner = new StubHandler(HttpStatusCode.UnprocessableEntity, HttpStatusCode.OK);

        var response = await SendAsync(inner, HttpMethod.Get);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal(1, inner.Attempts);
    }

    [Fact]
    public async Task ResendsTheBodyOfARetriedWrite()
    {
        var inner = new StubHandler(HttpStatusCode.TooManyRequests, HttpStatusCode.OK) { CaptureBodies = true };

        await SendAsync(inner, HttpMethod.Post);

        Assert.Equal(2, inner.Bodies.Count);
        Assert.All(inner.Bodies, body => Assert.Contains("test-pro", body));
    }

    private static async Task<HttpResponseMessage> SendAsync(StubHandler inner, HttpMethod method, int maxRetryAttempts = 3)
    {
        var settings = new MaxioSettings
        {
            MaxRetryAttempts = maxRetryAttempts,
            RetryBaseDelay = TimeSpan.FromMilliseconds(1)
        };

        var handler = new MaxioRetryHandler(Options.Create(settings), NullLogger<MaxioRetryHandler>.Instance)
        {
            InnerHandler = inner
        };

        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://example.invalid/") };
        using var request = new HttpRequestMessage(method, "subscriptions.json");

        if (method == HttpMethod.Post)
        {
            request.Content = JsonContent.Create(new { product_handle = "test-pro" });
        }

        return await client.SendAsync(request);
    }

    private static IEnumerable<object> Enumerable(HttpStatusCode statusCode, int count)
    {
        for (var i = 0; i < count; i++)
        {
            yield return statusCode;
        }
    }

    /// <summary>Replays a scripted sequence of statuses or exceptions, one per attempt.</summary>
    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly List<object> _script;

        public StubHandler(params object[] script) => _script = new List<object>(script);

        public StubHandler(IEnumerable<object> script) => _script = new List<object>(script);

        public int Attempts { get; private set; }

        public bool CaptureBodies { get; set; }

        public List<string> Bodies { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (CaptureBodies && request.Content is not null)
            {
                Bodies.Add(await request.Content.ReadAsStringAsync(cancellationToken));
            }

            var step = _script[Math.Min(Attempts, _script.Count - 1)];
            Attempts++;

            if (step is Exception exception)
            {
                throw exception;
            }

            return new HttpResponseMessage((HttpStatusCode)step) { RequestMessage = request };
        }
    }
}
