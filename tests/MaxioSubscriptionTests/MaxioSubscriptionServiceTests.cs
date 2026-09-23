using System.Net;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using MaxioAdvancedBilling.Core.Configuration;
using MaxioAdvancedBilling.Servers;
using Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints.Maxio;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;
using static Microsoft.eShopWeb.MaxioSubscriptionTests.RoutingStubHandler;

namespace Microsoft.eShopWeb.MaxioSubscriptionTests;

public class MaxioSubscriptionServiceTests
{
    private static readonly MaxioUserContext User = new("u1", "alice@example.com", "alice", "(eShopOnWeb)");

    private const string ProductsJson = """
        [ { "product": { "id": 1, "name": "Pro Plan", "handle": "eshop-pro",
              "description": "Pro", "price_in_cents": 29900, "interval": 1, "interval_unit": "month" } },
          { "product": { "id": 2, "name": "Basic Plan", "handle": "basic-plan",
              "description": "Basic", "price_in_cents": 2900, "interval": 1, "interval_unit": "month" } } ]
        """;

    private const string CustomerJson = """
        { "customer": { "id": 123, "reference": "eshop-user:u1", "email": "alice@example.com" } }
        """;

    private const string ActiveSubscriptionJson = """
        { "subscription": { "id": 55, "state": "active", "reference": "eshop-sub:u1:eshop-pro",
            "product": { "handle": "eshop-pro", "name": "Pro Plan", "price_in_cents": 29900 },
            "product_price_in_cents": 29900,
            "current_period_ends_at": "2026-10-23T00:00:00Z", "next_assessment_at": "2026-10-23T00:00:00Z" } }
        """;

    private const string ActiveSubscriptionListJson = """
        [ { "subscription": { "id": 55, "state": "active", "reference": "eshop-sub:u1:eshop-pro",
            "product": { "handle": "eshop-pro", "name": "Pro Plan", "price_in_cents": 29900 },
            "product_price_in_cents": 29900,
            "current_period_ends_at": "2026-10-23T00:00:00Z", "next_assessment_at": "2026-10-23T00:00:00Z" } } ]
        """;

    private static (MaxioSubscriptionService Svc, RoutingStubHandler Handler) Build(Func<HttpRequestMessage, Stub> router)
    {
        var handler = new RoutingStubHandler(router);
        var options = new MaxioAdvancedBillingClientOptions
        {
            Environment = ServerEnvironment.Us,
            BasicAuth = new BasicAuthCredentials { Username = "k", Password = "x" },
            Retry = RetryOptions.Disabled() // deterministic, fast tests — no backoff on retryable GETs
        };
        options.Server.Production.Us.BaseUrl = "https://test.chargify.com";
        var client = new MaxioAdvancedBillingClient(new HttpClient(handler), options);
        var settings = Options.Create(new MaxioSettings
        {
            ApiKey = "k",
            Subdomain = "test",
            ProductFamilyHandle = "eshop-subscribe"
        });
        var svc = new MaxioSubscriptionService(client, settings, NullLogger<MaxioSubscriptionService>.Instance);
        return (svc, handler);
    }

    // Route classification helpers.
    private static bool IsProducts(HttpRequestMessage r) => r.Method == HttpMethod.Get && r.RequestUri!.AbsolutePath.Contains("/products.json");
    private static bool IsCustomerLookup(HttpRequestMessage r) => r.Method == HttpMethod.Get && r.RequestUri!.AbsolutePath.Contains("/customers/lookup.json");
    private static bool IsCreateCustomer(HttpRequestMessage r) => r.Method == HttpMethod.Post && r.RequestUri!.AbsolutePath.EndsWith("/customers.json");
    private static bool IsListCustomerSubs(HttpRequestMessage r) => r.Method == HttpMethod.Get && r.RequestUri!.AbsolutePath.Contains("/customers/") && r.RequestUri.AbsolutePath.EndsWith("/subscriptions.json");
    private static bool IsCreateSubscription(HttpRequestMessage r) => r.Method == HttpMethod.Post && r.RequestUri!.AbsolutePath.EndsWith("/subscriptions.json");
    private static bool IsFindSubscription(HttpRequestMessage r) => r.Method == HttpMethod.Get && r.RequestUri!.AbsolutePath.Contains("/subscriptions/lookup.json");

    private static Stub NotFound() => new(HttpStatusCode.NotFound, "{}");

    [Fact]
    public async Task GetPlansAsync_maps_products_to_plans()
    {
        var (svc, _) = Build(r => IsProducts(r) ? new Stub(HttpStatusCode.OK, ProductsJson) : NotFound());

        var plans = await svc.GetPlansAsync(default);

        Assert.Equal(2, plans.Count);
        var pro = Assert.Single(plans, p => p.Handle == "eshop-pro");
        Assert.Equal("Pro Plan", pro.Name);
        Assert.Equal(29900, pro.PriceInCents);
        Assert.Equal("month", pro.IntervalUnit);
    }

    [Fact]
    public async Task SubscribeAsync_new_customer_creates_customer_then_subscription()
    {
        var (svc, handler) = Build(r =>
        {
            if (IsProducts(r)) return new Stub(HttpStatusCode.OK, ProductsJson);
            if (IsCustomerLookup(r)) return NotFound();                       // no customer yet
            if (IsCreateCustomer(r)) return new Stub(HttpStatusCode.OK, CustomerJson);
            if (IsListCustomerSubs(r)) return new Stub(HttpStatusCode.OK, "[]"); // no existing subs
            if (IsCreateSubscription(r)) return new Stub(HttpStatusCode.OK, ActiveSubscriptionJson);
            return NotFound();
        });

        var result = await svc.SubscribeAsync(User, "eshop-pro", default);

        Assert.False(result.AlreadySubscribed);
        Assert.Equal(55, result.Subscription.Id);
        Assert.Equal("active", result.Subscription.State);
        Assert.Equal(SubscriptionOutcome.Active, result.Subscription.Outcome);
        Assert.Equal("eshop-pro", result.Subscription.ProductHandle);
        Assert.Equal(1, handler.Count(HttpMethod.Post, "/customers.json"));
        Assert.Equal(1, handler.Count(HttpMethod.Post, "/subscriptions.json"));

        // The create subscription body carried the deterministic reference and the customer id.
        var subBody = handler.Bodies[^1];
        Assert.Contains("\"reference\":\"eshop-sub:u1:eshop-pro\"", subBody!.Replace(" ", ""));
        Assert.Contains("\"customer_id\":123", subBody.Replace(" ", ""));
    }

    [Fact]
    public async Task SubscribeAsync_existing_live_subscription_is_idempotent_and_does_not_recreate()
    {
        var (svc, handler) = Build(r =>
        {
            if (IsProducts(r)) return new Stub(HttpStatusCode.OK, ProductsJson);
            if (IsCustomerLookup(r)) return new Stub(HttpStatusCode.OK, CustomerJson); // customer exists
            if (IsListCustomerSubs(r)) return new Stub(HttpStatusCode.OK, ActiveSubscriptionListJson);
            if (IsCreateSubscription(r)) return new Stub(HttpStatusCode.OK, ActiveSubscriptionJson);
            return NotFound();
        });

        var result = await svc.SubscribeAsync(User, "eshop-pro", default);

        Assert.True(result.AlreadySubscribed);
        Assert.Equal(55, result.Subscription.Id);
        Assert.Equal(0, handler.Count(HttpMethod.Post, "/subscriptions.json")); // no second create
        Assert.Equal(0, handler.Count(HttpMethod.Post, "/customers.json"));     // customer already existed
    }

    [Fact]
    public async Task SubscribeAsync_duplicate_create_race_reconciles_via_find()
    {
        var (svc, handler) = Build(r =>
        {
            if (IsProducts(r)) return new Stub(HttpStatusCode.OK, ProductsJson);
            if (IsCustomerLookup(r)) return new Stub(HttpStatusCode.OK, CustomerJson);
            if (IsListCustomerSubs(r)) return new Stub(HttpStatusCode.OK, "[]"); // race: not visible yet
            if (IsCreateSubscription(r)) return new Stub(HttpStatusCode.UnprocessableEntity, """{ "errors": ["Reference: has already been taken"] }""");
            if (IsFindSubscription(r)) return new Stub(HttpStatusCode.OK, ActiveSubscriptionJson); // the winner
            return NotFound();
        });

        var result = await svc.SubscribeAsync(User, "eshop-pro", default);

        Assert.True(result.AlreadySubscribed);
        Assert.Equal(55, result.Subscription.Id);
        Assert.Equal(1, handler.Count(HttpMethod.Get, "/subscriptions/lookup.json"));
    }

    [Fact]
    public async Task SubscribeAsync_validation_error_without_existing_throws_caller_error()
    {
        var (svc, _) = Build(r =>
        {
            if (IsProducts(r)) return new Stub(HttpStatusCode.OK, ProductsJson);
            if (IsCustomerLookup(r)) return new Stub(HttpStatusCode.OK, CustomerJson);
            if (IsListCustomerSubs(r)) return new Stub(HttpStatusCode.OK, "[]");
            if (IsCreateSubscription(r)) return new Stub(HttpStatusCode.UnprocessableEntity, """{ "errors": ["Product: is not valid"] }""");
            if (IsFindSubscription(r)) return NotFound(); // genuinely nothing was created
            return NotFound();
        });

        var ex = await Assert.ThrowsAsync<MaxioIntegrationException>(() => svc.SubscribeAsync(User, "eshop-pro", default));
        Assert.True(ex.IsCallerError);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, ex.StatusCode);
        Assert.Contains("Product: is not valid", ex.Message);
    }

    [Fact]
    public async Task SubscribeAsync_unknown_plan_is_rejected_as_caller_error()
    {
        var (svc, handler) = Build(r => IsProducts(r) ? new Stub(HttpStatusCode.OK, ProductsJson) : NotFound());

        var ex = await Assert.ThrowsAsync<MaxioIntegrationException>(() => svc.SubscribeAsync(User, "no-such-plan", default));

        Assert.True(ex.IsCallerError);
        Assert.Equal(HttpStatusCode.BadRequest, ex.StatusCode);
        Assert.Equal(0, handler.Count(HttpMethod.Post, "/subscriptions.json")); // never attempted a write
    }

    [Fact]
    public async Task GetMySubscriptions_no_customer_returns_empty_without_creating_one()
    {
        var (svc, handler) = Build(r => IsCustomerLookup(r) ? NotFound() : NotFound());

        var subs = await svc.GetMySubscriptionsAsync(User, default);

        Assert.Empty(subs);
        Assert.Equal(0, handler.Count(HttpMethod.Post, "/customers.json")); // a read must not create a customer
    }

    [Fact]
    public async Task GetMySubscriptions_existing_customer_lists_subscriptions()
    {
        var (svc, _) = Build(r =>
        {
            if (IsCustomerLookup(r)) return new Stub(HttpStatusCode.OK, CustomerJson);
            if (IsListCustomerSubs(r)) return new Stub(HttpStatusCode.OK, ActiveSubscriptionListJson);
            return NotFound();
        });

        var subs = await svc.GetMySubscriptionsAsync(User, default);

        var sub = Assert.Single(subs);
        Assert.Equal(55, sub.Id);
        Assert.Equal("active", sub.State);
        Assert.Equal("eshop-pro", sub.ProductHandle);
    }

    [Fact]
    public async Task GetPlansAsync_provider_error_becomes_integration_exception()
    {
        var (svc, _) = Build(_ => new Stub(HttpStatusCode.InternalServerError, "boom"));

        await Assert.ThrowsAsync<MaxioIntegrationException>(() => svc.GetPlansAsync(default));
    }
}
