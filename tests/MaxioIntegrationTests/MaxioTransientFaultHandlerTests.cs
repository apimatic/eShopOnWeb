using System.Net;
using Microsoft.eShopWeb.MaxioIntegrationTests.Fakes;
using Microsoft.Extensions.Options;
using Xunit;

namespace Microsoft.eShopWeb.MaxioIntegrationTests;

/// <summary>
/// Transport resilience: a transient blip on a read must not reach the customer, retries must be
/// bounded, and a failed write must never be resent — resending risks a duplicate charge.
/// </summary>
public class MaxioTransientFaultHandlerTests
{
    [Fact]
    public async Task ATransientFailureOnAReadIsRetriedAndSucceeds()
    {
        var server = new FakeMaxioServer()
            .RespondInOrder(HttpMethod.Get, "product_families",
                (HttpStatusCode.ServiceUnavailable, null),
                (HttpStatusCode.OK, MaxioPayloads.ProductList));

        var plans = await BillingClientFactory.Create(server).ListPlansAsync();

        Assert.Equal(2, plans.Count);
        Assert.Equal(2, server.RequestsFor(HttpMethod.Get, "product_families").Count);
    }

    [Theory]
    [InlineData(HttpStatusCode.RequestTimeout)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.BadGateway)]
    [InlineData(HttpStatusCode.GatewayTimeout)]
    public async Task EveryTransientStatusIsRetriedUpToTheConfiguredBound(HttpStatusCode status)
    {
        var server = new FakeMaxioServer()
            .Respond(HttpMethod.Get, "subscriptions/15236915.json", status, "{}");

        await Assert.ThrowsAnyAsync<Exception>(
            () => BillingClientFactory.Create(server, settings => settings.MaxRetryAttempts = 3).GetSubscriptionAsync(15236915));

        Assert.Equal(3, server.Requests.Count);
    }

    [Fact]
    public async Task RetriesAreBoundedByTheConfiguredAttemptCount()
    {
        var server = new FakeMaxioServer()
            .Respond(HttpMethod.Get, "subscriptions/15236915.json", HttpStatusCode.ServiceUnavailable, "{}");

        await Assert.ThrowsAnyAsync<Exception>(
            () => BillingClientFactory.Create(server, settings => settings.MaxRetryAttempts = 2).GetSubscriptionAsync(15236915));

        Assert.Equal(2, server.Requests.Count);
    }

    [Fact]
    public async Task ANonTransientStatusIsNotRetried()
    {
        var server = new FakeMaxioServer()
            .Respond(HttpMethod.Get, "subscriptions/15236915.json", HttpStatusCode.NotFound, "{}");

        await BillingClientFactory.Create(server).GetSubscriptionAsync(15236915);

        Assert.Single(server.Requests);
    }

    [Fact]
    public async Task AFailedUsageReportIsNeverResent()
    {
        var server = new FakeMaxioServer()
            .Respond(HttpMethod.Post, "usages.json", HttpStatusCode.InternalServerError, "{}");

        await Assert.ThrowsAnyAsync<Exception>(
            () => BillingClientFactory.Create(server).RecordUsageAsync(15236915, "api-call", 5m, null));

        Assert.Single(server.RequestsFor(HttpMethod.Post, "usages.json"));
    }

    [Theory]
    [InlineData("POST", "subscriptions.json")]
    [InlineData("PUT", "subscriptions/15236915/reactivate.json")]
    [InlineData("DELETE", "subscriptions/15236915.json")]
    public async Task NoWriteIsEverResentAfterATransientFailure(string method, string path)
    {
        var httpMethod = new HttpMethod(method);
        var server = new FakeMaxioServer()
            .Respond(httpMethod, path, HttpStatusCode.ServiceUnavailable, "{}");
        var options = Options.Create(BillingClientFactory.DefaultSettings());

        using var httpClient = new HttpClient(BillingClientFactory.WithFaultHandling(server, options))
        {
            BaseAddress = new Uri("https://billing.test/")
        };

        using var response = await httpClient.SendAsync(new HttpRequestMessage(httpMethod, path));

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Single(server.Requests);
    }

    [Fact]
    public async Task ATransportFaultOnAReadIsRetriedBeforeGivingUp()
    {
        var server = new FakeMaxioServer()
            .Fail(HttpMethod.Get, "subscriptions/15236915.json", new HttpRequestException("connection reset"));

        await Assert.ThrowsAnyAsync<Exception>(
            () => BillingClientFactory.Create(server, settings => settings.MaxRetryAttempts = 3).GetSubscriptionAsync(15236915));

        Assert.Equal(3, server.Requests.Count);
    }

    [Fact]
    public async Task AThrottledReadWaitsForTheProvidersRetryAfterAndThenSucceeds()
    {
        var server = new RetryAfterServer(TimeSpan.FromMilliseconds(30));
        var options = Options.Create(BillingClientFactory.DefaultSettings());

        using var httpClient = new HttpClient(BillingClientFactory.WithFaultHandling(server, options))
        {
            BaseAddress = new Uri("https://billing.test/")
        };

        var started = DateTimeOffset.UtcNow;
        using var response = await httpClient.GetAsync("products.json");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, server.Attempts);
        Assert.True(DateTimeOffset.UtcNow - started >= TimeSpan.FromMilliseconds(25),
            "the client must wait out the provider's Retry-After rather than hammering it");
    }

    [Fact]
    public async Task AnAbsurdRetryAfterIsCappedSoTheCallerIsNeverStalled()
    {
        var server = new RetryAfterServer(TimeSpan.FromHours(1));
        var options = Options.Create(BillingClientFactory.DefaultSettings());
        options.Value.TimeoutSeconds = 1;

        using var httpClient = new HttpClient(BillingClientFactory.WithFaultHandling(server, options))
        {
            BaseAddress = new Uri("https://billing.test/")
        };

        using var response = await httpClient.GetAsync("products.json");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, server.Attempts);
    }

    [Fact]
    public async Task ARetriedRequestStillCarriesItsBodyAndCredentials()
    {
        var server = new FakeMaxioServer()
            .RespondInOrder(HttpMethod.Get, "product_families",
                (HttpStatusCode.ServiceUnavailable, null),
                (HttpStatusCode.OK, MaxioPayloads.ProductList));

        await BillingClientFactory.Create(server).ListPlansAsync();

        Assert.All(server.RequestsFor(HttpMethod.Get, "product_families"),
            request => Assert.Equal("Basic", request.Authorization?.Scheme));
    }
}
