#nullable enable
using System;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.Infrastructure.Billing.Maxio;
using Microsoft.eShopWeb.Infrastructure.Billing.Maxio.Contracts;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Microsoft.eShopWeb.IntegrationTests.Billing.Maxio;

public class MaxioResilienceHandlerTests
{
    private readonly StubHttpMessageHandler _inner = new();

    private MaxioApiClient CreateClient(int maxRetries = 3)
    {
        var resilience = new MaxioResilienceHandler(NullLogger<MaxioResilienceHandler>.Instance, maxRetries)
        {
            InnerHandler = _inner
        };

        var httpClient = new HttpClient(resilience) { BaseAddress = new Uri("https://acme.chargify.com/") };
        return new MaxioApiClient(httpClient, NullLogger<MaxioApiClient>.Instance);
    }

    [Fact]
    public async Task ThrottledReadsAreRetriedUntilTheyGetThrough()
    {
        _inner.Respond(HttpStatusCode.TooManyRequests, """{"errors":["usage violation"]}""")
            .Respond(HttpStatusCode.OK, """{"site":{"id":1,"currency":"USD"}}""");

        var site = await CreateClient().ReadSiteAsync();

        Assert.Equal("USD", site!.Currency);
        Assert.Equal(2, _inner.Requests.Count);
    }

    [Fact]
    public async Task TransientServerErrorsAreRetriedForReads()
    {
        _inner.Respond(HttpStatusCode.BadGateway)
            .Respond(HttpStatusCode.OK, """{"site":{"id":1,"currency":"USD"}}""");

        await CreateClient().ReadSiteAsync();

        Assert.Equal(2, _inner.Requests.Count);
    }

    [Fact]
    public async Task RetriesAreBoundedAndTheLastFailureIsSurfaced()
    {
        for (var i = 0; i < 3; i++)
        {
            _inner.Respond(HttpStatusCode.TooManyRequests, """{"errors":["usage violation"]}""");
        }

        var exception = await Assert.ThrowsAsync<BillingProviderException>(() => CreateClient(maxRetries: 2).ReadSiteAsync());

        Assert.Equal(429, exception.StatusCode);
        Assert.Equal(3, _inner.Requests.Count);
    }

    [Fact]
    public async Task AWriteCarryingAUniquenessTokenMayBeRetriedBecauseMaxioWillDeduplicateIt()
    {
        _inner.Respond(HttpStatusCode.ServiceUnavailable)
            .Respond(HttpStatusCode.OK, """{"subscription":{"id":42,"state":"active"}}""");

        var subscription = await CreateClient().CreateSubscriptionAsync(new CreateSubscriptionRequest
        {
            Subscription = new CreateSubscriptionAttributes { ProductHandle = "eshop-pro", CustomerId = 1 },
            UniquenessToken = "subscribe-abc"
        });

        Assert.Equal(42, subscription.Id);
        Assert.Equal(2, _inner.Requests.Count);
        // The retry has to carry the same body, or Maxio could not recognise it as the same submission.
        Assert.Equal(_inner.RequestBodies[0], _inner.RequestBodies[1]);
    }

    [Fact]
    public async Task AWriteWithoutAUniquenessTokenIsNeverRepeatedOnAServerError()
    {
        _inner.Respond(HttpStatusCode.ServiceUnavailable);

        await Assert.ThrowsAsync<BillingProviderException>(() =>
            CreateClient().CreateSubscriptionAsync(new CreateSubscriptionRequest
            {
                Subscription = new CreateSubscriptionAttributes { ProductHandle = "eshop-pro", CustomerId = 1 }
            }));

        Assert.Single(_inner.Requests);
    }

    [Fact]
    public async Task AThrottledWriteIsRetriedBecauseTheRequestWasRefusedRatherThanProcessed()
    {
        _inner.Respond(HttpStatusCode.TooManyRequests, """{"errors":["usage violation"]}""")
            .Respond(HttpStatusCode.OK, """{"subscription":{"id":7,"state":"active"}}""");

        var subscription = await CreateClient().CreateSubscriptionAsync(new CreateSubscriptionRequest
        {
            Subscription = new CreateSubscriptionAttributes { ProductHandle = "eshop-pro", CustomerId = 1 }
        });

        Assert.Equal(7, subscription.Id);
        Assert.Equal(2, _inner.Requests.Count);
    }

    [Fact]
    public async Task ATransportFailureOnAReadIsRetried()
    {
        _inner.Throw(new HttpRequestException("connection reset"))
            .Respond(HttpStatusCode.OK, """{"site":{"id":1,"currency":"USD"}}""");

        var site = await CreateClient().ReadSiteAsync();

        Assert.Equal("USD", site!.Currency);
        Assert.Equal(2, _inner.Requests.Count);
    }
}
