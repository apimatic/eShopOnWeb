using System.Net;
using Microsoft.eShopWeb.PublicApi.Maxio;
using Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.PublicApi.Maxio;

public class MaxioBillingServiceTests
{
    private const string UserName = "demouser@microsoft.com";

    private const string ProductsJson = """
        [
          { "product": { "id": 7126957, "name": "Pro Plan", "handle": "eshop-pro", "description": "Pro tier", "price_in_cents": 29900, "interval": 1, "interval_unit": "month", "initial_charge_in_cents": null, "trial_interval": null, "trial_interval_unit": null, "require_credit_card": false, "archived_at": null } },
          { "product": { "id": 7126958, "name": "Basic Plan", "handle": "basic-plan", "description": "Basic tier", "price_in_cents": 2900, "interval": 1, "interval_unit": "month", "initial_charge_in_cents": null, "trial_interval": null, "trial_interval_unit": null, "require_credit_card": false, "archived_at": null } },
          { "product": { "id": 42, "name": "Retired Plan", "handle": "retired", "description": null, "price_in_cents": 100, "interval": 1, "interval_unit": "month", "initial_charge_in_cents": null, "trial_interval": null, "trial_interval_unit": null, "require_credit_card": false, "archived_at": "2025-01-01T00:00:00Z" } }
        ]
        """;

    private const string CustomerJson = """
        { "customer": { "id": 12345, "reference": "demouser@microsoft.com", "email": "demouser@microsoft.com", "first_name": "Demouser", "last_name": "Customer" } }
        """;

    private const string SubscriptionJson = """
        { "subscription": { "id": 777, "state": "active", "reference": "eshoponweb:demouser@microsoft.com:eshop-pro", "product_price_in_cents": 29900, "current_period_ends_at": "2026-09-26T10:00:00-04:00", "next_assessment_at": "2026-09-26T10:00:00-04:00", "created_at": "2026-08-26T10:00:00-04:00", "product": { "id": 7126957, "name": "Pro Plan", "handle": "eshop-pro", "price_in_cents": 29900, "interval": 1, "interval_unit": "month" }, "customer": { "id": 12345, "reference": "demouser@microsoft.com" } } }
        """;

    private const string CanceledSubscriptionJson = """
        { "subscription": { "id": 776, "state": "canceled", "reference": "eshoponweb:demouser@microsoft.com:eshop-pro", "product_price_in_cents": 29900, "current_period_ends_at": null, "next_assessment_at": null, "created_at": "2026-07-01T10:00:00-04:00", "product": { "id": 7126957, "name": "Pro Plan", "handle": "eshop-pro", "price_in_cents": 29900, "interval": 1, "interval_unit": "month" }, "customer": { "id": 12345, "reference": "demouser@microsoft.com" } } }
        """;

    private static MaxioBillingService CreateService(StubHttpMessageHandler handler)
    {
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://test.chargify.com/") };
        var settings = Options.Create(new MaxioSettings
        {
            ApiKey = "test-key",
            Subdomain = "test",
            ProductFamilyHandle = "eshop-subscribe"
        });
        return new MaxioBillingService(httpClient, settings, NullLogger<MaxioBillingService>.Instance);
    }

    [Fact]
    public async Task ListPlans_MapsProductsAndSkipsArchived()
    {
        var handler = new StubHttpMessageHandler();
        handler.Route(HttpMethod.Get, "product_families/", StubHttpMessageHandler.Json(HttpStatusCode.OK, ProductsJson));

        var plans = await CreateService(handler).ListSubscriptionPlansAsync();

        Assert.Equal(2, plans.Count);
        var pro = plans.Single(p => p.Handle == "eshop-pro");
        Assert.Equal("Pro Plan", pro.Name);
        Assert.Equal(299.00m, pro.Price);
        Assert.Equal(1, pro.Interval);
        Assert.Equal("month", pro.IntervalUnit);
        Assert.DoesNotContain(plans, p => p.Handle == "retired");
        Assert.Contains("handle%3Aeshop-subscribe", handler.Requests[0].RequestUri!.PathAndQuery);
    }

    [Fact]
    public async Task Subscribe_CreatesCustomerAndSubscription_WhenNoneExist()
    {
        var handler = new StubHttpMessageHandler();
        handler.Route(HttpMethod.Get, "product_families/", StubHttpMessageHandler.Json(HttpStatusCode.OK, ProductsJson));
        // customer lookup -> 404 (default), subscription lookup -> 404 (default)
        handler.Route(HttpMethod.Post, "customers.json", StubHttpMessageHandler.Json(HttpStatusCode.OK, CustomerJson));
        handler.Route(HttpMethod.Post, "subscriptions.json", StubHttpMessageHandler.Json(HttpStatusCode.Created, SubscriptionJson));

        var result = await CreateService(handler).SubscribeAsync(UserName, "eshop-pro");

        Assert.True(result.Created);
        Assert.Equal(777, result.Subscription.Id);
        Assert.Equal("active", result.Subscription.State);
        Assert.Equal("eshop-pro", result.Subscription.PlanHandle);
        Assert.Equal("Pro Plan", result.Subscription.PlanName);
        Assert.Equal(299.00m, result.Subscription.Price);
        Assert.Equal(new DateTimeOffset(2026, 9, 26, 10, 0, 0, TimeSpan.FromHours(-4)), result.Subscription.NextBillingAt);

        // Customer created with the shopper's identity as the unique reference
        Assert.Contains("\"reference\":\"demouser@microsoft.com\"", handler.BodyOf(HttpMethod.Post, "customers.json"));
        // Subscription created by product handle + customer reference, with deterministic
        // reference reused as the duplicate-prevention uniqueness token
        var subscribeBody = handler.BodyOf(HttpMethod.Post, "subscriptions.json");
        Assert.Contains("\"product_handle\":\"eshop-pro\"", subscribeBody);
        Assert.Contains("\"customer_reference\":\"demouser@microsoft.com\"", subscribeBody);
        Assert.Contains("\"payment_collection_method\":\"remittance\"", subscribeBody);
        Assert.Contains("\"uniqueness_token\":\"eshoponweb:demouser@microsoft.com:eshop-pro\"", subscribeBody);
    }

    [Fact]
    public async Task Subscribe_ReturnsExistingSubscription_WhenOneIsLive()
    {
        var handler = new StubHttpMessageHandler();
        handler.Route(HttpMethod.Get, "product_families/", StubHttpMessageHandler.Json(HttpStatusCode.OK, ProductsJson));
        handler.Route(HttpMethod.Get, "customers/lookup.json", StubHttpMessageHandler.Json(HttpStatusCode.OK, CustomerJson));
        handler.Route(HttpMethod.Get, "subscriptions/lookup.json", StubHttpMessageHandler.Json(HttpStatusCode.OK, SubscriptionJson));

        var result = await CreateService(handler).SubscribeAsync(UserName, "eshop-pro");

        Assert.False(result.Created);
        Assert.Equal(777, result.Subscription.Id);
        Assert.False(handler.Requested(HttpMethod.Post, "subscriptions.json"), "A duplicate subscription must not be created.");
    }

    [Fact]
    public async Task Subscribe_CreatesNewSubscription_WhenExistingOneIsCanceled()
    {
        var handler = new StubHttpMessageHandler();
        handler.Route(HttpMethod.Get, "product_families/", StubHttpMessageHandler.Json(HttpStatusCode.OK, ProductsJson));
        handler.Route(HttpMethod.Get, "customers/lookup.json", StubHttpMessageHandler.Json(HttpStatusCode.OK, CustomerJson));
        handler.Route(HttpMethod.Get, "subscriptions/lookup.json", StubHttpMessageHandler.Json(HttpStatusCode.OK, CanceledSubscriptionJson));
        handler.Route(HttpMethod.Post, "subscriptions.json", StubHttpMessageHandler.Json(HttpStatusCode.Created, SubscriptionJson));

        var result = await CreateService(handler).SubscribeAsync(UserName, "eshop-pro");

        Assert.True(result.Created);
        // The new subscription gets a fresh reference so lookups keep resolving to the live one
        Assert.Contains("eshoponweb:demouser@microsoft.com:eshop-pro:", handler.BodyOf(HttpMethod.Post, "subscriptions.json"));
    }

    [Fact]
    public async Task Subscribe_ThrowsPlanNotFound_ForUnknownPlanHandle()
    {
        var handler = new StubHttpMessageHandler();
        handler.Route(HttpMethod.Get, "product_families/", StubHttpMessageHandler.Json(HttpStatusCode.OK, ProductsJson));

        await Assert.ThrowsAsync<SubscriptionPlanNotFoundException>(
            () => CreateService(handler).SubscribeAsync(UserName, "no-such-plan"));

        Assert.False(handler.Requested(HttpMethod.Post, "customers.json"), "No customer should be created for an unknown plan.");
        Assert.False(handler.Requested(HttpMethod.Post, "subscriptions.json"));
    }

    [Fact]
    public async Task Subscribe_RecoversExistingSubscription_OnDuplicateSubmissionConflict()
    {
        var handler = new StubHttpMessageHandler();
        handler.Route(HttpMethod.Get, "product_families/", StubHttpMessageHandler.Json(HttpStatusCode.OK, ProductsJson));
        handler.Route(HttpMethod.Get, "customers/lookup.json", StubHttpMessageHandler.Json(HttpStatusCode.OK, CustomerJson));
        // First lookup: not found (404 default). After the 409, the re-lookup finds it.
        var subscriptionLookupCalls = 0;
        handler.Route(HttpMethod.Get, "subscriptions/lookup.json", () =>
        {
            subscriptionLookupCalls++;
            return subscriptionLookupCalls < 2
                ? StubHttpMessageHandler.Json(HttpStatusCode.NotFound, "")
                : StubHttpMessageHandler.Json(HttpStatusCode.OK, SubscriptionJson);
        });
        handler.Route(HttpMethod.Post, "subscriptions.json",
            StubHttpMessageHandler.Json(HttpStatusCode.Conflict, "{\"errors\":[\"DuplicatePrevention::DuplicateSubmissionError\"]}"));

        var result = await CreateService(handler).SubscribeAsync(UserName, "eshop-pro");

        Assert.False(result.Created);
        Assert.Equal(777, result.Subscription.Id);
    }

    [Fact]
    public async Task Subscribe_RetriesWithFreshToken_WhenConflictedButNothingWasCreated()
    {
        var handler = new StubHttpMessageHandler();
        handler.Route(HttpMethod.Get, "product_families/", StubHttpMessageHandler.Json(HttpStatusCode.OK, ProductsJson));
        handler.Route(HttpMethod.Get, "customers/lookup.json", StubHttpMessageHandler.Json(HttpStatusCode.OK, CustomerJson));
        // Subscription lookups never find anything: the earlier request failed before creating.
        var createCalls = 0;
        handler.Route(HttpMethod.Post, "subscriptions.json", () =>
        {
            createCalls++;
            return createCalls < 2
                ? StubHttpMessageHandler.Json(HttpStatusCode.Conflict, "{\"errors\":[\"DuplicatePrevention::DuplicateSubmissionError\"]}")
                : StubHttpMessageHandler.Json(HttpStatusCode.Created, SubscriptionJson);
        });

        var result = await CreateService(handler).SubscribeAsync(UserName, "eshop-pro");

        Assert.True(result.Created);
        Assert.Equal(777, result.Subscription.Id);
        Assert.Equal(2, createCalls);
        // The retry keeps the deterministic subscription reference but uses a fresh uniqueness token
        var retryBody = handler.LastBodyOf(HttpMethod.Post, "subscriptions.json");
        Assert.Contains("\"reference\":\"eshoponweb:demouser@microsoft.com:eshop-pro\"", retryBody);
        Assert.Contains("\"uniqueness_token\":\"eshoponweb:demouser@microsoft.com:eshop-pro:", retryBody);
    }

    [Fact]
    public async Task Subscribe_ReusesCustomer_WhenCreateLosesRace()
    {
        var handler = new StubHttpMessageHandler();
        handler.Route(HttpMethod.Get, "product_families/", StubHttpMessageHandler.Json(HttpStatusCode.OK, ProductsJson));
        var customerLookupCalls = 0;
        handler.Route(HttpMethod.Get, "customers/lookup.json", () =>
        {
            customerLookupCalls++;
            return customerLookupCalls < 2
                ? StubHttpMessageHandler.Json(HttpStatusCode.NotFound, "")
                : StubHttpMessageHandler.Json(HttpStatusCode.OK, CustomerJson);
        });
        handler.Route(HttpMethod.Post, "customers.json",
            StubHttpMessageHandler.Json(HttpStatusCode.UnprocessableEntity, "{\"errors\":[\"Reference has already been taken\"]}"));
        handler.Route(HttpMethod.Post, "subscriptions.json", StubHttpMessageHandler.Json(HttpStatusCode.Created, SubscriptionJson));

        var result = await CreateService(handler).SubscribeAsync(UserName, "eshop-pro");

        Assert.True(result.Created);
        Assert.Equal(777, result.Subscription.Id);
    }

    [Fact]
    public async Task ListSubscriptions_ReturnsEmpty_WhenUserHasNoCustomer()
    {
        var handler = new StubHttpMessageHandler();
        // customer lookup -> 404 (default)

        var subscriptions = await CreateService(handler).ListSubscriptionsForUserAsync(UserName);

        Assert.Empty(subscriptions);
    }

    [Fact]
    public async Task ListSubscriptions_MapsSubscriptionsForCustomer()
    {
        var handler = new StubHttpMessageHandler();
        handler.Route(HttpMethod.Get, "customers/lookup.json", StubHttpMessageHandler.Json(HttpStatusCode.OK, CustomerJson));
        handler.Route(HttpMethod.Get, "customers/12345/subscriptions.json",
            StubHttpMessageHandler.Json(HttpStatusCode.OK, $"[{SubscriptionJson}]"));

        var subscriptions = await CreateService(handler).ListSubscriptionsForUserAsync(UserName);

        var subscription = Assert.Single(subscriptions);
        Assert.Equal(777, subscription.Id);
        Assert.Equal("active", subscription.State);
        Assert.Equal("eshop-pro", subscription.PlanHandle);
        Assert.Equal(new DateTimeOffset(2026, 9, 26, 10, 0, 0, TimeSpan.FromHours(-4)), subscription.NextBillingAt);
    }

    [Fact]
    public async Task ListPlans_ThrowsMaxioApiException_OnErrorResponse()
    {
        var handler = new StubHttpMessageHandler();
        handler.Route(HttpMethod.Get, "product_families/",
            StubHttpMessageHandler.Json(HttpStatusCode.UnprocessableEntity, "{\"errors\":[\"boom\"]}"));

        var exception = await Assert.ThrowsAsync<MaxioApiException>(
            () => CreateService(handler).ListSubscriptionPlansAsync());

        Assert.Equal(HttpStatusCode.UnprocessableEntity, exception.StatusCode);
        Assert.Contains("boom", exception.Message);
    }
}
