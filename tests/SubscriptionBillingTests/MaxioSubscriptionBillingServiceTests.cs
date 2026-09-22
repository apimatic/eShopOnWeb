using System.Net;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Configuration;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Microsoft.eShopWeb.SubscriptionBillingTests;

public class MaxioSubscriptionBillingServiceTests
{
    private const string FamilyHandle = "test-product-family";

    private const string Families =
        "[{\"product_family\":{\"id\":4242,\"handle\":\"test-product-family\",\"name\":\"eShop Subscriptions\"}}]";

    private const string Products =
        "[{\"product\":{\"id\":1,\"handle\":\"eshop-pro\",\"name\":\"Pro Plan\",\"description\":\"Pro\",\"price_in_cents\":29900,\"interval\":1,\"interval_unit\":\"month\",\"require_credit_card\":false}}," +
        "{\"product\":{\"id\":2,\"handle\":\"basic-plan\",\"name\":\"Basic\",\"price_in_cents\":2900,\"interval\":1,\"interval_unit\":\"month\"}}," +
        "{\"product\":{\"id\":3,\"handle\":\"old-plan\",\"name\":\"Old\",\"price_in_cents\":100,\"archived_at\":\"2020-01-01T00:00:00Z\"}}]";

    private const string Customer =
        "{\"customer\":{\"id\":555,\"reference\":\"user-1\",\"email\":\"user-1@example.com\"}}";

    private const string ActiveSubscription =
        "{\"subscription\":{\"id\":999,\"state\":\"active\",\"product\":{\"handle\":\"eshop-pro\",\"name\":\"Pro Plan\"}," +
        "\"product_price_in_cents\":29900,\"current_period_ends_at\":\"2026-10-22T12:00:00Z\",\"reference\":\"eshopweb-user-1\"}}";

    private const string NotFound = "{\"error\":\"not found\"}";

    private static readonly SubscriberIdentity Subscriber = new("user-1", "user-1@example.com", "user", "eShopOnWeb");

    private static MaxioSubscriptionBillingService CreateService(RouteStubHandler handler)
    {
        var options = new MaxioAdvancedBillingClientOptions { Retry = RetryOptions.Disabled() };
        var client = new MaxioAdvancedBillingClient(new HttpClient(handler), options);
        var settings = Options.Create(new MaxioSettings
        {
            ApiKey = "k",
            Subdomain = "s",
            ProductFamilyHandle = FamilyHandle,
        });
        return new MaxioSubscriptionBillingService(client, settings, NullLogger<MaxioSubscriptionBillingService>.Instance);
    }

    private static bool IsFamilies(string p) => p == "/product_families.json";
    private static bool IsProducts(string p) => p.StartsWith("/product_families/") && p.EndsWith("/products.json");
    private static bool IsCustomerLookup(string p) => p == "/customers/lookup.json";
    private static bool IsCreateCustomer(string p) => p == "/customers.json";
    private static bool IsCustomerSubs(string p) => p.StartsWith("/customers/") && p.EndsWith("/subscriptions.json");
    private static bool IsFindSubscription(string p) => p == "/subscriptions/lookup.json";
    private static bool IsCreateSubscription(string p) => p == "/subscriptions.json";

    [Fact]
    public async Task GetPlans_MapsProducts_AndExcludesArchived()
    {
        var handler = new RouteStubHandler()
            .On(HttpMethod.Get, IsFamilies, HttpStatusCode.OK, Families)
            .On(HttpMethod.Get, IsProducts, HttpStatusCode.OK, Products);
        var service = CreateService(handler);

        var plans = await service.GetPlansAsync();

        Assert.Equal(2, plans.Count); // archived "old-plan" excluded
        var pro = Assert.Single(plans, p => p.Handle == "eshop-pro");
        Assert.Equal("Pro Plan", pro.Name);
        Assert.Equal(29900, pro.PriceInCents);
        Assert.Equal("month", pro.IntervalUnit);
        Assert.Equal(1, pro.Interval);
        Assert.False(pro.RequiresPaymentMethod);
        Assert.DoesNotContain(plans, p => p.Handle == "old-plan");
    }

    [Fact]
    public async Task Subscribe_NewSubscription_CreatesCustomerAndSubscription()
    {
        var handler = new RouteStubHandler()
            .On(HttpMethod.Get, IsFamilies, HttpStatusCode.OK, Families)
            .On(HttpMethod.Get, IsProducts, HttpStatusCode.OK, Products)
            .On(HttpMethod.Get, IsCustomerLookup, HttpStatusCode.NotFound, NotFound)
            .On(HttpMethod.Post, IsCreateCustomer, HttpStatusCode.Created, Customer)
            .On(HttpMethod.Get, IsFindSubscription, HttpStatusCode.NotFound, NotFound)
            .On(HttpMethod.Post, IsCreateSubscription, HttpStatusCode.Created, ActiveSubscription);
        var service = CreateService(handler);

        var result = await service.SubscribeAsync(Subscriber, "eshop-pro");

        Assert.False(result.AlreadySubscribed);
        Assert.Equal(555, result.CustomerId);
        Assert.Equal(999, result.Subscription.Id);
        Assert.Equal("active", result.Subscription.State);
        Assert.True(result.Subscription.IsLive);
        Assert.Equal("eshop-pro", result.Subscription.PlanHandle);
        Assert.Equal(1, handler.CountRequests(HttpMethod.Post, "/customers.json"));
        Assert.Equal(1, handler.CountRequests(HttpMethod.Post, "/subscriptions.json"));
    }

    [Fact]
    public async Task Subscribe_WhenLiveSubscriptionExists_IsIdempotent_AndDoesNotCreate()
    {
        var handler = new RouteStubHandler()
            .On(HttpMethod.Get, IsFamilies, HttpStatusCode.OK, Families)
            .On(HttpMethod.Get, IsProducts, HttpStatusCode.OK, Products)
            .On(HttpMethod.Get, IsCustomerLookup, HttpStatusCode.OK, Customer)
            .On(HttpMethod.Get, IsFindSubscription, HttpStatusCode.OK, ActiveSubscription)
            .On(HttpMethod.Post, IsCreateSubscription, HttpStatusCode.Created, ActiveSubscription);
        var service = CreateService(handler);

        var result = await service.SubscribeAsync(Subscriber, "eshop-pro");

        Assert.True(result.AlreadySubscribed);
        Assert.Equal(999, result.Subscription.Id);
        Assert.Equal(0, handler.CountRequests(HttpMethod.Post, "/subscriptions.json")); // no duplicate create
    }

    [Fact]
    public async Task Subscribe_WhenCreateReturns422ButSubscriptionExists_ReconcilesToExisting()
    {
        // Pre-check find returns 404; create returns 422 (duplicate race); reconcile find returns the live sub.
        var findCalls = 0;
        var handler = new RouteStubHandler()
            .On(HttpMethod.Get, IsFamilies, HttpStatusCode.OK, Families)
            .On(HttpMethod.Get, IsProducts, HttpStatusCode.OK, Products)
            .On(HttpMethod.Get, IsCustomerLookup, HttpStatusCode.OK, Customer)
            .On(r => r.Method == HttpMethod.Get && IsFindSubscription(r.RequestUri!.AbsolutePath),
                _ => System.Threading.Interlocked.Increment(ref findCalls) == 1
                    ? RouteStubHandler.JsonResponse(HttpStatusCode.NotFound, NotFound)
                    : RouteStubHandler.JsonResponse(HttpStatusCode.OK, ActiveSubscription))
            .On(HttpMethod.Post, IsCreateSubscription, HttpStatusCode.UnprocessableEntity,
                "{\"errors\":[\"Reference: has already been taken.\"]}");
        var service = CreateService(handler);

        var result = await service.SubscribeAsync(Subscriber, "eshop-pro");

        Assert.True(result.AlreadySubscribed);
        Assert.Equal(999, result.Subscription.Id);
    }

    [Fact]
    public async Task Subscribe_UnknownPlan_Throws400()
    {
        var handler = new RouteStubHandler()
            .On(HttpMethod.Get, IsFamilies, HttpStatusCode.OK, Families)
            .On(HttpMethod.Get, IsProducts, HttpStatusCode.OK, Products);
        var service = CreateService(handler);

        var ex = await Assert.ThrowsAsync<SubscriptionBillingException>(
            () => service.SubscribeAsync(Subscriber, "does-not-exist"));
        Assert.Equal(400, ex.StatusCode);
        Assert.Equal(0, handler.CountRequests(HttpMethod.Post, "/subscriptions.json"));
    }

    [Fact]
    public async Task Subscribe_MissingPlan_Throws400()
    {
        var service = CreateService(new RouteStubHandler());
        var ex = await Assert.ThrowsAsync<SubscriptionBillingException>(
            () => service.SubscribeAsync(Subscriber, null));
        Assert.Equal(400, ex.StatusCode);
    }

    [Fact]
    public async Task GetSubscriptions_NoCustomer_ReturnsEmpty()
    {
        var handler = new RouteStubHandler()
            .On(HttpMethod.Get, IsCustomerLookup, HttpStatusCode.NotFound, NotFound);
        var service = CreateService(handler);

        var subs = await service.GetSubscriptionsAsync(Subscriber);

        Assert.Empty(subs);
    }

    [Fact]
    public async Task GetSubscriptions_ReturnsMappedSubscriptions()
    {
        var handler = new RouteStubHandler()
            .On(HttpMethod.Get, IsCustomerLookup, HttpStatusCode.OK, Customer)
            .On(HttpMethod.Get, IsCustomerSubs, HttpStatusCode.OK, $"[{ActiveSubscription}]");
        var service = CreateService(handler);

        var subs = await service.GetSubscriptionsAsync(Subscriber);

        var sub = Assert.Single(subs);
        Assert.Equal(999, sub.Id);
        Assert.Equal("active", sub.State);
        Assert.True(sub.IsLive);
        Assert.Equal("eshop-pro", sub.PlanHandle);
    }

    [Fact]
    public async Task GetSubscriptions_ProviderError_MapsTo502()
    {
        var handler = new RouteStubHandler()
            .On(HttpMethod.Get, IsCustomerLookup, HttpStatusCode.InternalServerError, "{\"error\":\"boom\"}");
        var service = CreateService(handler);

        var ex = await Assert.ThrowsAsync<SubscriptionBillingException>(
            () => service.GetSubscriptionsAsync(Subscriber));
        Assert.Equal(502, ex.StatusCode);
    }
}
