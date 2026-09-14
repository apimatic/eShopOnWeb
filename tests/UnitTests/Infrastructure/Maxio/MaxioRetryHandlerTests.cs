using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Maxio;

public class MaxioRetryHandlerTests
{
    private static HttpClient CreateClient(CountingHandler inner, int maxRetryAttempts = 1)
    {
        var retryHandler = new MaxioRetryHandler(
            Options.Create(new MaxioSettings { MaxRetryAttempts = maxRetryAttempts }),
            NullLogger<MaxioRetryHandler>.Instance)
        {
            InnerHandler = inner
        };

        return new HttpClient(retryHandler) { BaseAddress = new Uri("https://maxio.stub") };
    }

    [Fact]
    public async Task RetriesAThrottledWriteBecauseItWasNeverProcessed()
    {
        var inner = new CountingHandler(HttpStatusCode.TooManyRequests, HttpStatusCode.Created);
        var client = CreateClient(inner);

        var response = await client.PostAsync("/subscriptions.json", new StringContent("{}"));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(2, inner.Calls);
    }

    [Fact]
    public async Task DoesNotRetryAWriteThatFailedOnTheServer()
    {
        var inner = new CountingHandler(HttpStatusCode.InternalServerError, HttpStatusCode.Created);
        var client = CreateClient(inner);

        var response = await client.PostAsync("/subscriptions.json", new StringContent("{}"));

        // Repeating it could enroll the shopper twice, so the failure is surfaced instead.
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal(1, inner.Calls);
    }

    [Fact]
    public async Task RetriesATransientServerFailureOnReads()
    {
        var inner = new CountingHandler(HttpStatusCode.ServiceUnavailable, HttpStatusCode.OK);
        var client = CreateClient(inner);

        var response = await client.GetAsync("/site.json");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, inner.Calls);
    }

    [Fact]
    public async Task GivesUpAfterTheConfiguredNumberOfAttempts()
    {
        var inner = new CountingHandler(
            HttpStatusCode.ServiceUnavailable,
            HttpStatusCode.ServiceUnavailable,
            HttpStatusCode.ServiceUnavailable);
        var client = CreateClient(inner, maxRetryAttempts: 1);

        var response = await client.GetAsync("/site.json");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal(2, inner.Calls);
    }

    [Fact]
    public async Task DoesNotRetryASuccessfulOrRejectedCall()
    {
        var inner = new CountingHandler(HttpStatusCode.UnprocessableEntity);
        var client = CreateClient(inner);

        var response = await client.PostAsync("/customers.json", new StringContent("{}"));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal(1, inner.Calls);
    }

    private sealed class CountingHandler : HttpMessageHandler
    {
        private readonly Queue<HttpStatusCode> _statusCodes;

        public CountingHandler(params HttpStatusCode[] statusCodes) => _statusCodes = new Queue<HttpStatusCode>(statusCodes);

        public int Calls { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            var statusCode = _statusCodes.Count > 0 ? _statusCodes.Dequeue() : HttpStatusCode.OK;
            return Task.FromResult(new HttpResponseMessage(statusCode));
        }
    }
}
