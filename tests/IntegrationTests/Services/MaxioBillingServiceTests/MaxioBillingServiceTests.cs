#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Servers;
using Microsoft.eShopWeb;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Services;
using Microsoft.Extensions.Caching.Memory;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.IntegrationTests.Services.MaxioBillingServiceTests;

/// <summary>
/// Tests for <see cref="MaxioBillingService"/> against a stubbed HTTP transport —
/// no real Maxio calls are made. Each stubbed response matches the SDK's wire shapes.
/// </summary>
public class MaxioBillingServiceTests
{
    private const string UserId = "demouser@microsoft.com";
    private const string ProductHandle = "eshop-pro";

    [Fact]
    public async Task ListPlans_ReturnsMappedPlans()
    {
        var handler = new StubHandler();
        handler.EnqueueJson(HttpStatusCode.OK, """[{"product_family":{"id":3023074,"handle":"eshop-subscribe","name":"eShop Subscribe"}}]""");
        handler.EnqueueJson(HttpStatusCode.OK, """[{"product":{"id":7126957,"handle":"eshop-pro","name":"Pro Plan","price_in_cents":29900,"interval":1,"interval_unit":"month"}}]""");
        var service = CreateService(handler);

        var plans = await service.ListPlansAsync();

        var plan = Assert.Single(plans);
        Assert.Equal("eshop-pro", plan.Handle);
        Assert.Equal("Pro Plan", plan.Name);
        Assert.Equal(29900, plan.PriceInCents);
        Assert.Equal(1, plan.Interval);
        Assert.Equal("month", plan.IntervalUnit);
    }

    [Fact]
    public async Task Subscribe_CreatesCustomerThenSubscription_WhenCustomerMissing()
    {
        var handler = new StubHandler();
        handler.EnqueueJson(HttpStatusCode.NotFound, """{"errors":"not found"}""");                 // read customer by reference
        handler.EnqueueJson(HttpStatusCode.Created, """{"customer":{"id":555,"reference":"demouser@microsoft.com","email":"demouser@microsoft.com"}}""");
        handler.EnqueueJson(HttpStatusCode.OK, """[]""");                                            // list subscriptions (dedupe)
        handler.EnqueueJson(HttpStatusCode.Created, SubscriptionJson());                             // create subscription
        var service = CreateService(handler);

        var subscription = await service.SubscribeAsync(UserId, UserId, ProductHandle);

        Assert.Equal(900, subscription.Id);
        Assert.Equal("active", subscription.State);
        Assert.Equal("eshop-pro", subscription.ProductHandle);
        Assert.NotNull(subscription.NextBillingAt);
        Assert.Contains(handler.Requests, r => r.Method == HttpMethod.Post && r.Path!.Contains("/customers"));
        var createCall = Assert.Single(handler.Requests.Where(r => r.Method == HttpMethod.Post && r.Path!.Contains("/subscriptions")));
        Assert.Contains("\"payment_collection_method\":\"remittance\"", createCall.Body);
    }

    [Fact]
    public async Task Subscribe_ReturnsExisting_WhenLiveSubscriptionExists()
    {
        var handler = new StubHandler();
        handler.EnqueueJson(HttpStatusCode.OK, """{"customer":{"id":555,"reference":"demouser@microsoft.com"}}""");
        handler.EnqueueJson(HttpStatusCode.OK, """[{"subscription":{"id":900,"state":"active","product":{"handle":"eshop-pro","name":"Pro Plan"}}}]""");
        var service = CreateService(handler);

        var subscription = await service.SubscribeAsync(UserId, UserId, ProductHandle);

        Assert.Equal(900, subscription.Id);
        Assert.DoesNotContain(handler.Requests, r => r.Method == HttpMethod.Post && r.Path!.Contains("/subscriptions"));
    }

    [Fact]
    public async Task Subscribe_ReReadsCustomer_WhenCreateLosesReferenceRace()
    {
        var handler = new StubHandler();
        handler.EnqueueJson(HttpStatusCode.NotFound, """{"errors":"not found"}""");                 // read: absent
        handler.EnqueueJson(HttpStatusCode.UnprocessableEntity, """{"errors":{}}""");               // create: 422 (race lost)
        handler.EnqueueJson(HttpStatusCode.OK, """{"customer":{"id":555,"reference":"demouser@microsoft.com"}}"""); // re-read: winner
        handler.EnqueueJson(HttpStatusCode.OK, """[]""");
        handler.EnqueueJson(HttpStatusCode.Created, SubscriptionJson());
        var service = CreateService(handler);

        var subscription = await service.SubscribeAsync(UserId, UserId, ProductHandle);

        Assert.Equal(900, subscription.Id);
        Assert.Equal(2, handler.Requests.Count(r => r.Method == HttpMethod.Get && r.Path!.Contains("/customers") && !r.Path.Contains("subscriptions"))); // read + re-read
        Assert.Single(handler.Requests.Where(r => r.Method == HttpMethod.Post && r.Path!.Contains("/customers")));
    }

    [Fact]
    public async Task Subscribe_ThrowsBillingExceptionWithProviderStatus_WhenProviderRejects422()
    {
        var handler = new StubHandler();
        handler.EnqueueJson(HttpStatusCode.OK, """{"customer":{"id":555,"reference":"demouser@microsoft.com"}}""");
        handler.EnqueueJson(HttpStatusCode.OK, """[]""");
        handler.EnqueueJson(HttpStatusCode.UnprocessableEntity, """{"errors":["Product: could not be found"]}""");
        var service = CreateService(handler);

        var ex = await Assert.ThrowsAsync<BillingException>(() => service.SubscribeAsync(UserId, UserId, ProductHandle));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, ex.ProviderStatusCode);
        Assert.Contains("Product: could not be found", ex.Message);
    }

    [Fact]
    public async Task Subscribe_ReconcilesToExistingSubscription_AfterTransportFailureOnWrite()
    {
        var handler = new StubHandler();
        handler.EnqueueJson(HttpStatusCode.OK, """{"customer":{"id":555,"reference":"demouser@microsoft.com"}}""");
        handler.EnqueueJson(HttpStatusCode.OK, """[]""");                                            // dedupe: none yet
        handler.EnqueueThrow(new HttpRequestException("connection reset"));                          // create attempt 1
        handler.EnqueueThrow(new HttpRequestException("connection reset"));                          // create attempt 2 (transport retry)
        handler.EnqueueThrow(new HttpRequestException("connection reset"));                          // create attempt 3
        handler.EnqueueThrow(new HttpRequestException("connection reset"));                          // create attempt 4
        handler.EnqueueJson(HttpStatusCode.OK, """{"customer":{"id":555,"reference":"demouser@microsoft.com"}}""");  // reconcile: read customer
        handler.EnqueueJson(HttpStatusCode.OK, """[{"subscription":{"id":900,"state":"active","product":{"handle":"eshop-pro","name":"Pro Plan"}}}]""");
        var service = CreateService(handler);

        var subscription = await service.SubscribeAsync(UserId, UserId, ProductHandle);

        Assert.Equal(900, subscription.Id);
    }

    [Fact]
    public async Task ListMySubscriptions_ReturnsEmpty_WhenUserHasNoCustomer()
    {
        var handler = new StubHandler();
        handler.EnqueueJson(HttpStatusCode.NotFound, """{"errors":"not found"}""");
        var service = CreateService(handler);

        var subscriptions = await service.ListMySubscriptionsAsync(UserId);

        Assert.Empty(subscriptions);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task ListMySubscriptions_ReturnsMappedSubscriptions()
    {
        var handler = new StubHandler();
        handler.EnqueueJson(HttpStatusCode.OK, """{"customer":{"id":555,"reference":"demouser@microsoft.com"}}""");
        handler.EnqueueJson(HttpStatusCode.OK, """[{"subscription":{"id":900,"state":"active","product":{"handle":"eshop-pro","name":"Pro Plan"},"product_price_in_cents":29900,"next_assessment_at":"2026-09-26T00:00:00Z"}}]""");
        var service = CreateService(handler);

        var subscriptions = await service.ListMySubscriptionsAsync(UserId);

        var subscription = Assert.Single(subscriptions);
        Assert.Equal(900, subscription.Id);
        Assert.Equal("active", subscription.State);
        Assert.Equal(29900, subscription.PriceInCents);
        Assert.Equal(new DateTimeOffset(2026, 9, 26, 0, 0, 0, TimeSpan.Zero), subscription.NextBillingAt);
    }

    private static string SubscriptionJson() =>
        """{"subscription":{"id":900,"state":"active","product":{"handle":"eshop-pro","name":"Pro Plan"},"product_price_in_cents":29900,"next_assessment_at":"2026-09-26T00:00:00Z","current_period_ends_at":"2026-09-26T00:00:00Z"}}""";

    private static MaxioBillingService CreateService(StubHandler handler)
    {
        var client = new MaxioAdvancedBillingClient(
            new HttpClient(handler),
            new MaxioAdvancedBillingClientOptions { Environment = ServerEnvironment.Us });

        var options = new MaxioOptions
        {
            ApiKey = "test-key",
            Subdomain = "test-site",
            ProductFamilyHandle = "eshop-subscribe"
        };

        return new MaxioBillingService(
            client,
            options,
            new MemoryCache(new MemoryCacheOptions()),
            Substitute.For<IAppLogger<MaxioBillingService>>());
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Queue<Func<HttpResponseMessage>> _responders = new();

        public List<(HttpMethod Method, string? Path, string? Body)> Requests { get; } = new();

        public void EnqueueJson(HttpStatusCode status, string json)
            => _responders.Enqueue(() => new HttpResponseMessage(status)
            {
                Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json")
            });

        public void EnqueueThrow(Exception exception)
            => _responders.Enqueue(() => throw exception);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? null : await request.Content.ReadAsStringAsync();
            Requests.Add((request.Method, request.RequestUri?.AbsolutePath, body));

            if (_responders.Count == 0)
            {
                throw new InvalidOperationException($"Unexpected request: {request.Method} {request.RequestUri}");
            }

            return _responders.Dequeue()();
        }
    }
}
