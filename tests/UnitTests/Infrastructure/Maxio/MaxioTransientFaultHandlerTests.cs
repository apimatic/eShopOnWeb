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

public class MaxioTransientFaultHandlerTests
{
    private static HttpClient CreateClient(StubHandler stub, int maxRetryAttempts = 3)
    {
        var settings = new MaxioSettings
        {
            ApiKey = "test-key",
            Subdomain = "acme",
            ProductFamilyHandle = "eshop-subscribe",
            MaxRetryAttempts = maxRetryAttempts
        };

        var handler = new MaxioTransientFaultHandler(
            new StaticOptionsMonitor(settings),
            NullLogger<MaxioTransientFaultHandler>.Instance)
        {
            InnerHandler = stub
        };

        return new HttpClient(handler) { BaseAddress = new Uri("https://acme.chargify.com/") };
    }

    [Fact]
    public async Task RetriesAThrottledRequestUntilItSucceeds()
    {
        var stub = new StubHandler(HttpStatusCode.TooManyRequests, HttpStatusCode.TooManyRequests, HttpStatusCode.OK);

        var response = await CreateClient(stub).GetAsync("site.json");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(3, stub.CallCount);
    }

    [Fact]
    public async Task RetriesAServerErrorOnAReadButNotOnAWrite()
    {
        var reads = new StubHandler(HttpStatusCode.InternalServerError, HttpStatusCode.OK);
        var readResponse = await CreateClient(reads).GetAsync("site.json");

        var writes = new StubHandler(HttpStatusCode.InternalServerError, HttpStatusCode.OK);
        var writeResponse = await CreateClient(writes).PostAsync("subscriptions.json", new StringContent("{}"));

        Assert.Equal(HttpStatusCode.OK, readResponse.StatusCode);
        Assert.Equal(2, reads.CallCount);

        // A 500 on a POST may well have created the record, so it is surfaced rather than repeated.
        Assert.Equal(HttpStatusCode.InternalServerError, writeResponse.StatusCode);
        Assert.Equal(1, writes.CallCount);
    }

    [Fact]
    public async Task RetriesAWriteWhenTheGatewayNeverReachedTheApi()
    {
        var stub = new StubHandler(HttpStatusCode.BadGateway, HttpStatusCode.Created);

        var response = await CreateClient(stub).PostAsync("subscriptions.json", new StringContent("{}"));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(2, stub.CallCount);
    }

    [Fact]
    public async Task DoesNotRetryAValidationFailure()
    {
        var stub = new StubHandler(HttpStatusCode.UnprocessableEntity, HttpStatusCode.OK);

        var response = await CreateClient(stub).PostAsync("subscriptions.json", new StringContent("{}"));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal(1, stub.CallCount);
    }

    [Fact]
    public async Task GivesUpAfterTheConfiguredNumberOfAttempts()
    {
        var stub = new StubHandler(
            HttpStatusCode.ServiceUnavailable,
            HttpStatusCode.ServiceUnavailable,
            HttpStatusCode.ServiceUnavailable,
            HttpStatusCode.ServiceUnavailable,
            HttpStatusCode.OK);

        var response = await CreateClient(stub, maxRetryAttempts: 2).GetAsync("site.json");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal(3, stub.CallCount);
    }

    [Fact]
    public async Task DoesNotRetryWhenRetriesAreTurnedOff()
    {
        var stub = new StubHandler(HttpStatusCode.TooManyRequests, HttpStatusCode.OK);

        var response = await CreateClient(stub, maxRetryAttempts: 0).GetAsync("site.json");

        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
        Assert.Equal(1, stub.CallCount);
    }

    [Fact]
    public async Task ResendsTheRequestBodyOnRetry()
    {
        var stub = new StubHandler(HttpStatusCode.BadGateway, HttpStatusCode.Created);

        await CreateClient(stub).PostAsync("subscriptions.json", new StringContent("""{"subscription":{}}"""));

        Assert.Equal(2, stub.Bodies.Count);
        Assert.All(stub.Bodies, body => Assert.Equal("""{"subscription":{}}""", body));
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode[] _statusCodes;

        public StubHandler(params HttpStatusCode[] statusCodes) => _statusCodes = statusCodes;

        public int CallCount { get; private set; }

        public List<string> Bodies { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.Content is not null)
            {
                Bodies.Add(await request.Content.ReadAsStringAsync(cancellationToken));
            }

            var statusCode = _statusCodes[Math.Min(CallCount, _statusCodes.Length - 1)];
            CallCount++;

            return new HttpResponseMessage(statusCode);
        }
    }

    private sealed class StaticOptionsMonitor : IOptionsMonitor<MaxioSettings>
    {
        public StaticOptionsMonitor(MaxioSettings settings) => CurrentValue = settings;

        public MaxioSettings CurrentValue { get; }

        public MaxioSettings Get(string? name) => CurrentValue;

        public IDisposable? OnChange(Action<MaxioSettings, string?> listener) => null;
    }
}
