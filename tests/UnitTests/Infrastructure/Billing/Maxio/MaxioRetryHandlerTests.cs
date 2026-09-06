using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.Infrastructure.Billing.Maxio.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Billing.Maxio;

public class MaxioRetryHandlerTests
{
    [Fact]
    public async Task RetriesAThrottledGetUntilItSucceeds()
    {
        var inner = new SequencedHandler(HttpStatusCode.TooManyRequests, HttpStatusCode.TooManyRequests, HttpStatusCode.OK);

        var response = await SendAsync(inner, HttpMethod.Get, maxRetryAttempts: 3);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(3, inner.Calls);
    }

    [Fact]
    public async Task RetriesAThrottledPostBecauseAThrottledRequestWasNeverProcessed()
    {
        var inner = new SequencedHandler(HttpStatusCode.TooManyRequests, HttpStatusCode.Created);

        var response = await SendAsync(inner, HttpMethod.Post, maxRetryAttempts: 3);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(2, inner.Calls);
    }

    [Fact]
    public async Task RetriesAFailedGetBecauseReadingAgainIsHarmless()
    {
        var inner = new SequencedHandler(HttpStatusCode.BadGateway, HttpStatusCode.OK);

        var response = await SendAsync(inner, HttpMethod.Get, maxRetryAttempts: 3);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, inner.Calls);
    }

    [Fact]
    public async Task DoesNotRetryAFailedPostBecauseTheWriteMayHaveLanded()
    {
        var inner = new SequencedHandler(HttpStatusCode.InternalServerError, HttpStatusCode.Created);

        var response = await SendAsync(inner, HttpMethod.Post, maxRetryAttempts: 3);

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal(1, inner.Calls);
    }

    [Fact]
    public async Task GivesUpAfterTheConfiguredNumberOfRetries()
    {
        var inner = new SequencedHandler(
            HttpStatusCode.TooManyRequests, HttpStatusCode.TooManyRequests,
            HttpStatusCode.TooManyRequests, HttpStatusCode.TooManyRequests);

        var response = await SendAsync(inner, HttpMethod.Get, maxRetryAttempts: 2);

        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
        Assert.Equal(3, inner.Calls); // one attempt plus two retries
    }

    [Fact]
    public async Task ResendsTheRequestBodyOnEveryAttempt()
    {
        var inner = new SequencedHandler(HttpStatusCode.TooManyRequests, HttpStatusCode.Created);

        await SendAsync(inner, HttpMethod.Post, maxRetryAttempts: 3, body: "{\"subscription\":{}}");

        Assert.Equal(new[] { "{\"subscription\":{}}", "{\"subscription\":{}}" }, inner.Bodies);
    }

    [Fact]
    public async Task DoesNotRetryASuccessfulCall()
    {
        var inner = new SequencedHandler(HttpStatusCode.OK);

        await SendAsync(inner, HttpMethod.Get, maxRetryAttempts: 3);

        Assert.Equal(1, inner.Calls);
    }

    private static async Task<HttpResponseMessage> SendAsync(
        SequencedHandler inner, HttpMethod method, int maxRetryAttempts, string? body = null)
    {
        var handler = new MaxioRetryHandler(maxRetryAttempts, NullLogger<MaxioRetryHandler>.Instance)
        {
            InnerHandler = inner,
        };

        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://acme.chargify.com/") };
        using var request = new HttpRequestMessage(method, "subscriptions.json");

        if (body is not null)
        {
            request.Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json");
        }

        return await client.SendAsync(request);
    }

    /// <summary>Answers each call with the next status in a fixed sequence, recording the bodies seen.</summary>
    private sealed class SequencedHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode[] _statuses;

        public SequencedHandler(params HttpStatusCode[] statuses) => _statuses = statuses;

        public int Calls { get; private set; }

        public List<string> Bodies { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.Content is not null)
            {
                Bodies.Add(await request.Content.ReadAsStringAsync(cancellationToken));
            }

            var status = _statuses[Math.Min(Calls, _statuses.Length - 1)];
            Calls++;

            // Retry-After: 0 keeps the backoff out of the test's runtime.
            var response = new HttpResponseMessage(status);
            if (status == HttpStatusCode.TooManyRequests)
            {
                response.Headers.TryAddWithoutValidation("Retry-After", "0");
            }

            return response;
        }
    }
}
