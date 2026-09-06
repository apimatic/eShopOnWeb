using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using Microsoft.eShopWeb.Infrastructure.Maxio.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Maxio;

public class MaxioRetryHandlerSend
{
    private static HttpClient BuildClient(CountingHandler inner, int maxRetryAttempts = 3)
    {
        var settings = new MaxioSettings
        {
            ApiKey = "key",
            Subdomain = "acme",
            ProductFamilyHandle = "family",
            MaxRetryAttempts = maxRetryAttempts,
            RetryBaseDelayMilliseconds = 1
        };

        var retry = new MaxioRetryHandler(new StaticOptionsMonitor(settings), NullLogger<MaxioRetryHandler>.Instance)
        {
            InnerHandler = inner
        };

        return new HttpClient(retry) { BaseAddress = new Uri("https://acme.chargify.com/") };
    }

    [Fact]
    public async Task RetriesAServerFailureOnAReadAndEventuallySucceeds()
    {
        var inner = new CountingHandler(HttpStatusCode.InternalServerError, HttpStatusCode.BadGateway, HttpStatusCode.OK);

        var response = await BuildClient(inner).GetAsync("site.json");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(3, inner.Calls);
    }

    [Fact]
    public async Task GivesUpAfterTheConfiguredNumberOfAttempts()
    {
        var inner = new CountingHandler(Repeat(HttpStatusCode.ServiceUnavailable, 10));

        var response = await BuildClient(inner, maxRetryAttempts: 2).GetAsync("site.json");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal(3, inner.Calls);
    }

    [Fact]
    public async Task DoesNotReplayANonIdempotentWriteAfterAServerFailure()
    {
        var inner = new CountingHandler(HttpStatusCode.InternalServerError, HttpStatusCode.OK);

        // The subscription may already have been created before the 500 surfaced, so replaying it could enroll twice.
        var response = await BuildClient(inner).PostAsync("subscriptions.json", new StringContent("{}"));

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal(1, inner.Calls);
    }

    [Fact]
    public async Task ReplaysAThrottledWriteBecauseItWasNeverProcessed()
    {
        var inner = new CountingHandler(HttpStatusCode.TooManyRequests, HttpStatusCode.Created);

        var response = await BuildClient(inner).PostAsync("subscriptions.json", new StringContent("{}"));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(2, inner.Calls);
    }

    [Fact]
    public async Task RetriesATransportFailureOnARead()
    {
        var inner = new CountingHandler(HttpStatusCode.OK) { ThrowOnFirstCall = true };

        var response = await BuildClient(inner).GetAsync("site.json");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, inner.Calls);
    }

    [Fact]
    public async Task DoesNotRetryASuccessfulOrRejectedResponse()
    {
        var inner = new CountingHandler(HttpStatusCode.UnprocessableEntity);

        var response = await BuildClient(inner).PostAsync("subscriptions.json", new StringContent("{}"));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal(1, inner.Calls);
    }

    private static HttpStatusCode[] Repeat(HttpStatusCode status, int count)
    {
        var statuses = new HttpStatusCode[count];
        Array.Fill(statuses, status);
        return statuses;
    }

    private sealed class CountingHandler : HttpMessageHandler
    {
        private readonly Queue<HttpStatusCode> _responses;

        public CountingHandler(params HttpStatusCode[] responses)
        {
            _responses = new Queue<HttpStatusCode>(responses);
        }

        public int Calls { get; private set; }

        public bool ThrowOnFirstCall { get; set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;

            if (ThrowOnFirstCall && Calls == 1)
            {
                throw new HttpRequestException("connection reset");
            }

            var status = _responses.Count > 0 ? _responses.Dequeue() : HttpStatusCode.OK;
            return Task.FromResult(new HttpResponseMessage(status));
        }
    }

    private sealed class StaticOptionsMonitor : IOptionsMonitor<MaxioSettings>
    {
        public StaticOptionsMonitor(MaxioSettings value) => CurrentValue = value;

        public MaxioSettings CurrentValue { get; }

        public MaxioSettings Get(string? name) => CurrentValue;

        public IDisposable? OnChange(Action<MaxioSettings, string?> listener) => null;
    }
}
