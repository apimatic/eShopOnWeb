using System.Net;
using global::Maxio;
using global::Maxio.Core.Configuration;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Subscriptions;

/// <summary>
/// Exercises <see cref="MaxioSubscriptionBillingService"/> through the SDK's HttpClient seam. The stub
/// handler routes by Chargify route so real behaviour (idempotency, mapping, error translation) is
/// tested end-to-end through the SDK, not mocked at the service boundary.
/// </summary>
public class MaxioSubscriptionBillingServiceTests
{
    // Arbitrary stub values — the stub routes by path, not by these handles, so they need not match
    // any real catalog (and no real configuration value is embedded in the tests).
    private const string FamilyHandle = "test-family";
    private const string ProPlanHandle = "pro-plan";

    private static readonly SubscriberIdentity Subscriber = new("demouser@microsoft.com", "demouser@microsoft.com");

    private const string ProductsJson = """
        [
          { "product": { "id": 1, "handle": "pro-plan", "name": "Pro Plan", "description": "Pro",
            "price_in_cents": 29900, "interval": 1, "interval_unit": "month" } },
          { "product": { "id": 2, "handle": "basic-plan", "name": "Basic Plan", "description": "Basic",
            "price_in_cents": 2900, "interval": 1, "interval_unit": "month" } }
        ]
        """;

    private const string CustomerFoundJson =
        """{ "customer": { "id": 555, "reference": "demouser@microsoft.com", "email": "demouser@microsoft.com" } }""";

    private const string SubscriptionJson = """
        { "subscription": { "id": 901, "state": "active",
          "product": { "handle": "pro-plan", "name": "Pro Plan", "price_in_cents": 29900 },
          "product_price_in_cents": 29900,
          "current_period_ends_at": "2026-10-03T00:00:00Z",
          "next_assessment_at": "2026-10-03T00:00:00Z" } }
        """;

    private static (MaxioSubscriptionBillingService Service, StubHttpMessageHandler Handler) CreateService(
        Func<HttpRequestMessage, string?, (HttpStatusCode, string)> responder)
    {
        var handler = new StubHttpMessageHandler(responder);
        // Disable retries so error-path tests do not incur backoff delays and request counts are exact.
        var client = new MaxioClient(new HttpClient(handler),
            new MaxioClientOptions { Retry = RetryOptions.Disabled() });
        var settings = Options.Create(new MaxioSettings
        {
            ApiKey = "test-key",
            Subdomain = "test-site",
            ProductFamilyHandle = FamilyHandle,
            DefaultPlanHandle = ProPlanHandle,
            TimeoutSeconds = 30,
        });
        var service = new MaxioSubscriptionBillingService(client, settings, NullLogger<MaxioSubscriptionBillingService>.Instance);
        return (service, handler);
    }

    private static bool Is(HttpRequestMessage r, HttpMethod method, string pathContains) =>
        r.Method == method && (r.RequestUri?.AbsolutePath.Contains(pathContains, StringComparison.OrdinalIgnoreCase) ?? false);

    [Fact]
    public async Task GetPlans_MapsProductsToPlans()
    {
        var (service, _) = CreateService((r, _) =>
            Is(r, HttpMethod.Get, "/products.json")
                ? (HttpStatusCode.OK, ProductsJson)
                : (HttpStatusCode.NotFound, "{}"));

        var plans = await service.GetPlansAsync(CancellationToken.None);

        Assert.Equal(2, plans.Count);
        var pro = Assert.Single(plans, p => p.Handle == ProPlanHandle);
        Assert.Equal("Pro Plan", pro.Name);
        Assert.Equal(29900, pro.PriceInCents);
        Assert.Equal("month", pro.IntervalUnit);
    }

    [Fact]
    public async Task Subscribe_NewCustomer_CreatesCustomerThenSubscription()
    {
        var (service, handler) = CreateService((r, _) =>
        {
            if (Is(r, HttpMethod.Get, "/products.json")) return (HttpStatusCode.OK, ProductsJson);
            if (Is(r, HttpMethod.Get, "lookup.json")) return (HttpStatusCode.NotFound, "{}"); // no customer yet
            if (Is(r, HttpMethod.Post, "/customers.json")) return (HttpStatusCode.Created, CustomerFoundJson);
            if (Is(r, HttpMethod.Get, "/subscriptions.json")) return (HttpStatusCode.OK, "[]"); // no existing subs
            if (Is(r, HttpMethod.Post, "/subscriptions.json")) return (HttpStatusCode.Created, SubscriptionJson);
            return (HttpStatusCode.NotFound, "{}");
        });

        var result = await service.SubscribeAsync(Subscriber, ProPlanHandle, CancellationToken.None);

        Assert.False(result.AlreadySubscribed);
        Assert.Equal(901, result.Subscription.Id);
        Assert.Equal("active", result.Subscription.State);
        Assert.Equal(ProPlanHandle, result.Subscription.PlanHandle);
        Assert.NotNull(result.Subscription.NextBillingDate);

        // A customer and a subscription were each created exactly once.
        Assert.Equal(1, handler.CountByMethodAndPath(HttpMethod.Post, "/customers.json"));
        Assert.Equal(1, handler.CountByMethodAndPath(HttpMethod.Post, "/subscriptions.json"));

        // The subscription request referenced the plan by handle and the resolved customer id.
        var subBody = handler.Bodies[^1];
        Assert.Contains("\"product_handle\":\"pro-plan\"", subBody);
        Assert.Contains("\"customer_id\":555", subBody);
    }

    [Fact]
    public async Task Subscribe_ExistingCustomer_DoesNotCreateCustomer()
    {
        var (service, handler) = CreateService((r, _) =>
        {
            if (Is(r, HttpMethod.Get, "/products.json")) return (HttpStatusCode.OK, ProductsJson);
            if (Is(r, HttpMethod.Get, "lookup.json")) return (HttpStatusCode.OK, CustomerFoundJson); // customer exists
            if (Is(r, HttpMethod.Get, "/subscriptions.json")) return (HttpStatusCode.OK, "[]");
            if (Is(r, HttpMethod.Post, "/subscriptions.json")) return (HttpStatusCode.Created, SubscriptionJson);
            return (HttpStatusCode.NotFound, "{}");
        });

        var result = await service.SubscribeAsync(Subscriber, ProPlanHandle, CancellationToken.None);

        Assert.False(result.AlreadySubscribed);
        Assert.Equal(0, handler.CountByMethodAndPath(HttpMethod.Post, "/customers.json"));
        Assert.Equal(1, handler.CountByMethodAndPath(HttpMethod.Post, "/subscriptions.json"));
    }

    [Fact]
    public async Task Subscribe_WhenLiveSubscriptionExists_IsIdempotentNoOp()
    {
        var existingSubs = """
            [ { "subscription": { "id": 900, "state": "active",
                "product": { "handle": "pro-plan", "name": "Pro Plan", "price_in_cents": 29900 },
                "product_price_in_cents": 29900,
                "current_period_ends_at": "2026-10-03T00:00:00Z" } } ]
            """;

        var (service, handler) = CreateService((r, _) =>
        {
            if (Is(r, HttpMethod.Get, "/products.json")) return (HttpStatusCode.OK, ProductsJson);
            if (Is(r, HttpMethod.Get, "lookup.json")) return (HttpStatusCode.OK, CustomerFoundJson);
            if (Is(r, HttpMethod.Get, "/subscriptions.json")) return (HttpStatusCode.OK, existingSubs);
            if (Is(r, HttpMethod.Post, "/subscriptions.json")) return (HttpStatusCode.Created, SubscriptionJson);
            return (HttpStatusCode.NotFound, "{}");
        });

        var result = await service.SubscribeAsync(Subscriber, ProPlanHandle, CancellationToken.None);

        Assert.True(result.AlreadySubscribed);
        Assert.Equal(900, result.Subscription.Id);
        // No new subscription is created when a live one already exists.
        Assert.Equal(0, handler.CountByMethodAndPath(HttpMethod.Post, "/subscriptions.json"));
    }

    [Fact]
    public async Task Subscribe_UnknownPlan_ThrowsValidationWithoutCreating()
    {
        var (service, handler) = CreateService((r, _) =>
            Is(r, HttpMethod.Get, "/products.json") ? (HttpStatusCode.OK, ProductsJson) : (HttpStatusCode.NotFound, "{}"));

        var ex = await Assert.ThrowsAsync<SubscriptionBillingException>(
            () => service.SubscribeAsync(Subscriber, "no-such-plan", CancellationToken.None));

        Assert.Equal(BillingErrorKind.Validation, ex.Kind);
        Assert.Equal(0, handler.CountByMethodAndPath(HttpMethod.Post, "/subscriptions.json"));
        Assert.Equal(0, handler.CountByMethodAndPath(HttpMethod.Post, "/customers.json"));
    }

    [Fact]
    public async Task Subscribe_CreateSubscription422_ThrowsValidation()
    {
        var (service, _) = CreateService((r, _) =>
        {
            if (Is(r, HttpMethod.Get, "/products.json")) return (HttpStatusCode.OK, ProductsJson);
            if (Is(r, HttpMethod.Get, "lookup.json")) return (HttpStatusCode.OK, CustomerFoundJson);
            if (Is(r, HttpMethod.Get, "/subscriptions.json")) return (HttpStatusCode.OK, "[]");
            if (Is(r, HttpMethod.Post, "/subscriptions.json"))
                return (HttpStatusCode.UnprocessableEntity, """{ "errors": ["Payment method required"] }""");
            return (HttpStatusCode.NotFound, "{}");
        });

        var ex = await Assert.ThrowsAsync<SubscriptionBillingException>(
            () => service.SubscribeAsync(Subscriber, ProPlanHandle, CancellationToken.None));

        Assert.Equal(BillingErrorKind.Validation, ex.Kind);
        Assert.Contains("Payment method required", ex.Message);
    }

    [Fact]
    public async Task GetSubscriptions_WhenNoCustomer_ReturnsEmpty()
    {
        var (service, handler) = CreateService((r, _) =>
            Is(r, HttpMethod.Get, "lookup.json") ? (HttpStatusCode.NotFound, "{}") : (HttpStatusCode.NotFound, "{}"));

        var subs = await service.GetSubscriptionsAsync(Subscriber, CancellationToken.None);

        Assert.Empty(subs);
        // Never listed subscriptions because there is no customer.
        Assert.Equal(0, handler.CountByMethodAndPath(HttpMethod.Get, "/subscriptions.json"));
    }

    [Fact]
    public async Task GetPlans_ProviderError_ThrowsProviderUnavailable()
    {
        var (service, _) = CreateService((_, _) => (HttpStatusCode.InternalServerError, "boom"));

        var ex = await Assert.ThrowsAsync<SubscriptionBillingException>(
            () => service.GetPlansAsync(CancellationToken.None));

        Assert.Equal(BillingErrorKind.ProviderUnavailable, ex.Kind);
    }
}
