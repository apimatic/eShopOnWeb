using System.Net;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Configuration;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Billing;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Billing;

public class MaxioSubscriptionServiceTests
{
    private const string CustomerJson = """{"customer":{"id":7,"reference":"user-1","email":"demouser@microsoft.com"}}""";
    private const string SubscriptionJson = """{"subscription":{"id":9,"state":"active","product_price_in_cents":29900,"current_period_ends_at":"2026-09-24T00:00:00Z","product":{"handle":"eshop-pro","name":"Pro Plan"}}}""";

    private static readonly SubscribeCommand Command = new SubscribeCommand
    {
        UserId = "user-1",
        Email = "demouser@microsoft.com",
        FirstName = "demouser",
        LastName = "Shopper",
        ProductHandle = "eshop-pro"
    };

    private static MaxioSubscriptionService CreateService(StubHandler handler)
    {
        var client = new MaxioAdvancedBillingClient(new HttpClient(handler), new MaxioAdvancedBillingClientOptions
        {
            Retry = RetryOptions.Default() with { MaxRetries = 1 }
        });
        return new MaxioSubscriptionService(
            client,
            Options.Create(new MaxioSettings { ProductFamilyHandle = "fam" }),
            new MemoryCache(new MemoryCacheOptions()),
            NullLogger<MaxioSubscriptionService>.Instance);
    }

    [Fact]
    public async Task ListPlansResolvesFamilyByHandleAndMapsProducts()
    {
        var handler = new StubHandler(request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.Contains("products"))
            {
                return StubHandler.Json("""[{"product":{"handle":"eshop-pro","name":"Pro Plan","price_in_cents":29900,"interval":1,"interval_unit":"month"}}]""");
            }
            return StubHandler.Json("""[{"product_family":{"id":5,"handle":"fam","name":"Family"}}]""");
        });
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
    public async Task SubscribeCreatesCustomerWhenAbsentThenCreatesSubscription()
    {
        var handler = new StubHandler(request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (request.Method == HttpMethod.Get && path.Contains("customers"))
            {
                return new HttpResponseMessage(HttpStatusCode.NotFound); // no customer yet
            }
            if (request.Method == HttpMethod.Post && path.Contains("customers"))
            {
                return StubHandler.Json(CustomerJson, HttpStatusCode.Created);
            }
            if (request.Method == HttpMethod.Get && path.Contains("subscriptions"))
            {
                return new HttpResponseMessage(HttpStatusCode.NotFound); // no existing subscription
            }
            if (request.Method == HttpMethod.Post && path.Contains("subscriptions"))
            {
                return StubHandler.Json(SubscriptionJson, HttpStatusCode.Created);
            }
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });
        var service = CreateService(handler);

        var subscription = await service.SubscribeAsync(Command);

        Assert.Equal(9, subscription.Id);
        Assert.Equal("active", subscription.State);
        Assert.Equal("eshop-pro", subscription.ProductHandle);
        Assert.Equal(29900, subscription.PriceInCents);
        Assert.Equal(new DateTimeOffset(2026, 9, 24, 0, 0, 0, TimeSpan.Zero), subscription.NextBillingDate);
        Assert.Single(handler.Requests.Where(r => r.Method == HttpMethod.Post && r.RequestUri!.AbsolutePath.Contains("customers")));
        Assert.Single(handler.Requests.Where(r => r.Method == HttpMethod.Post && r.RequestUri!.AbsolutePath.Contains("subscriptions")));
    }

    [Fact]
    public async Task SubscribeReturnsExistingSubscriptionOnDoubleSubmit()
    {
        var handler = new StubHandler(request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (request.Method == HttpMethod.Get && path.Contains("customers"))
            {
                return StubHandler.Json(CustomerJson);
            }
            if (request.Method == HttpMethod.Get && path.Contains("subscriptions"))
            {
                return StubHandler.Json(SubscriptionJson); // already subscribed
            }
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });
        var service = CreateService(handler);

        var subscription = await service.SubscribeAsync(Command);

        Assert.Equal(9, subscription.Id);
        Assert.Equal("active", subscription.State);
        Assert.DoesNotContain(handler.Requests, r => r.Method == HttpMethod.Post);
    }

    [Fact]
    public async Task SubscribeRecoversFromDuplicateCustomerRace()
    {
        var customerLookups = 0;
        var handler = new StubHandler(request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (request.Method == HttpMethod.Get && path.Contains("customers"))
            {
                customerLookups++;
                // Absent before the create, present after the 422 race.
                return customerLookups == 1
                    ? new HttpResponseMessage(HttpStatusCode.NotFound)
                    : StubHandler.Json(CustomerJson);
            }
            if (request.Method == HttpMethod.Post && path.Contains("customers"))
            {
                return StubHandler.Json("""{"errors":{}}""", HttpStatusCode.UnprocessableEntity);
            }
            if (request.Method == HttpMethod.Get && path.Contains("subscriptions"))
            {
                return new HttpResponseMessage(HttpStatusCode.NotFound);
            }
            if (request.Method == HttpMethod.Post && path.Contains("subscriptions"))
            {
                return StubHandler.Json(SubscriptionJson, HttpStatusCode.Created);
            }
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });
        var service = CreateService(handler);

        var subscription = await service.SubscribeAsync(Command);

        Assert.Equal(9, subscription.Id);
        Assert.Single(handler.Requests.Where(r => r.Method == HttpMethod.Post && r.RequestUri!.AbsolutePath.Contains("customers")));
    }

    [Fact]
    public async Task ListSubscriptionsReturnsEmptyWhenUserHasNoCustomer()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound));
        var service = CreateService(handler);

        var subscriptions = await service.ListSubscriptionsAsync("user-1");

        Assert.Empty(subscriptions);
    }

    [Fact]
    public async Task ListSubscriptionsMapsProviderSubscriptions()
    {
        var handler = new StubHandler(request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.Contains("customers") && !path.Contains("subscriptions"))
            {
                return StubHandler.Json(CustomerJson);
            }
            return StubHandler.Json($"[{SubscriptionJson}]");
        });
        var service = CreateService(handler);

        var subscriptions = await service.ListSubscriptionsAsync("user-1");

        var subscription = Assert.Single(subscriptions);
        Assert.Equal(9, subscription.Id);
        Assert.Equal("active", subscription.State);
        Assert.Equal("eshop-pro", subscription.ProductHandle);
        Assert.Equal(new DateTimeOffset(2026, 9, 24, 0, 0, 0, TimeSpan.Zero), subscription.NextBillingDate);
    }
}
