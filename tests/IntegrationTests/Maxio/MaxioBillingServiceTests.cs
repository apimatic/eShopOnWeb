using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using MaxioAdvancedBilling;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Microsoft.eShopWeb.IntegrationTests.Maxio;

/// <summary>
/// Behaviour tests for <see cref="MaxioBillingService"/> using the SDK's HttpClient test seam —
/// no real network calls. They assert the integration's real behaviour (mapping, idempotency,
/// error translation), not that a call merely executed.
/// </summary>
public class MaxioBillingServiceTests
{
    private const string UserName = "demouser@microsoft.com";

    private const string ProProductJson =
        """{ "product": { "id": 7126957, "name": "Pro Plan", "handle": "eshop-pro", "description": "Pro tier", "price_in_cents": 29900, "interval": 1, "interval_unit": "month" } }""";

    private const string BasicProductJson =
        """{ "product": { "id": 7126958, "name": "Basic Plan", "handle": "basic-plan", "price_in_cents": 2900, "interval": 1, "interval_unit": "month" } }""";

    private static string ProductsListJson => $"[{ProProductJson},{BasicProductJson}]";

    private static string CustomerJson(int id) =>
        $$"""{ "customer": { "id": {{id}}, "reference": "eshoponweb:demouser@microsoft.com" } }""";

    private static string SubscriptionJson(int id, string state, string planHandle) =>
        $$"""{ "subscription": { "id": {{id}}, "state": "{{state}}", "product_price_in_cents": 29900, "current_period_ends_at": "2026-10-10T00:00:00Z", "next_assessment_at": "2026-10-10T00:00:00Z", "product": { "handle": "{{planHandle}}", "name": "Pro Plan" } } }""";

    private static MaxioBillingService BuildService(StubHttpMessageHandler handler)
    {
        var client = new MaxioAdvancedBillingClient(new HttpClient(handler), new MaxioAdvancedBillingClientOptions());
        var settings = Options.Create(new MaxioSettings { ProductFamilyHandle = "test-family", Subdomain = "test-subdomain" });
        return new MaxioBillingService(client, settings, NullLogger<MaxioBillingService>.Instance);
    }

    private static bool IsProducts(HttpRequestMessage r) => r.Method == HttpMethod.Get && r.RequestUri!.AbsolutePath.EndsWith("/products.json");
    private static bool IsLookup(HttpRequestMessage r) => r.Method == HttpMethod.Get && r.RequestUri!.AbsolutePath.EndsWith("/customers/lookup.json");
    private static bool IsCreateCustomer(HttpRequestMessage r) => r.Method == HttpMethod.Post && r.RequestUri!.AbsolutePath == "/customers.json";
    private static bool IsListSubs(HttpRequestMessage r) => r.Method == HttpMethod.Get && r.RequestUri!.AbsolutePath.Contains("/customers/") && r.RequestUri!.AbsolutePath.EndsWith("/subscriptions.json");
    private static bool IsCreateSub(HttpRequestMessage r) => r.Method == HttpMethod.Post && r.RequestUri!.AbsolutePath == "/subscriptions.json";

    [Fact]
    public async Task GetPlansAsync_maps_family_products_to_plans()
    {
        var handler = new StubHttpMessageHandler(_ => (HttpStatusCode.OK, ProductsListJson));
        var service = BuildService(handler);

        var plans = await service.GetPlansAsync();

        Assert.Equal(2, plans.Count);
        var pro = plans.Single(p => p.Handle == "eshop-pro");
        Assert.Equal("Pro Plan", pro.Name);
        Assert.Equal(29900, pro.PriceInCents);
        Assert.Equal("month", pro.IntervalUnit);
    }

    [Fact]
    public async Task SubscribeAsync_creates_customer_and_subscription_when_none_exist()
    {
        var handler = new StubHttpMessageHandler(r =>
        {
            if (IsProducts(r)) return (HttpStatusCode.OK, ProductsListJson);
            if (IsLookup(r)) return (HttpStatusCode.NotFound, "{}");           // no customer yet
            if (IsCreateCustomer(r)) return (HttpStatusCode.Created, CustomerJson(555));
            if (IsListSubs(r)) return (HttpStatusCode.OK, "[]");               // no subscriptions yet
            if (IsCreateSub(r)) return (HttpStatusCode.Created, SubscriptionJson(1, "active", "eshop-pro"));
            return (HttpStatusCode.InternalServerError, "{}");
        });
        var service = BuildService(handler);

        var result = await service.SubscribeAsync(UserName, "eshop-pro");

        Assert.False(result.AlreadyActive);
        Assert.Equal(1, result.Subscription.Id);
        Assert.Equal("active", result.Subscription.State);
        Assert.Equal("eshop-pro", result.Subscription.PlanHandle);
        Assert.Equal(1, handler.CountOf(HttpMethod.Post, "/customers.json"));
        Assert.Equal(1, handler.CountOf(HttpMethod.Post, "/subscriptions.json"));
    }

    [Fact]
    public async Task SubscribeAsync_is_idempotent_when_live_subscription_already_exists()
    {
        var handler = new StubHttpMessageHandler(r =>
        {
            if (IsProducts(r)) return (HttpStatusCode.OK, ProductsListJson);
            if (IsLookup(r)) return (HttpStatusCode.OK, CustomerJson(555));    // customer exists
            if (IsListSubs(r)) return (HttpStatusCode.OK, $"[{SubscriptionJson(42, "active", "eshop-pro")}]");
            if (IsCreateSub(r)) return (HttpStatusCode.Created, SubscriptionJson(99, "active", "eshop-pro"));
            return (HttpStatusCode.InternalServerError, "{}");
        });
        var service = BuildService(handler);

        var result = await service.SubscribeAsync(UserName, "eshop-pro");

        Assert.True(result.AlreadyActive);
        Assert.Equal(42, result.Subscription.Id);
        // No second subscription created (double-click safe).
        Assert.Equal(0, handler.CountOf(HttpMethod.Post, "/subscriptions.json"));
    }

    [Fact]
    public async Task SubscribeAsync_rejects_a_plan_outside_the_configured_family()
    {
        var handler = new StubHttpMessageHandler(r =>
            IsProducts(r) ? (HttpStatusCode.OK, ProductsListJson) : (HttpStatusCode.InternalServerError, "{}"));
        var service = BuildService(handler);

        var ex = await Assert.ThrowsAsync<BillingException>(() => service.SubscribeAsync(UserName, "not-a-real-plan"));

        Assert.Equal(404, ex.ProviderStatusCode);
        // No customer or subscription was ever created for an invalid plan.
        Assert.Equal(0, handler.CountOf(HttpMethod.Post, "/customers.json"));
        Assert.Equal(0, handler.CountOf(HttpMethod.Post, "/subscriptions.json"));
    }

    [Fact]
    public async Task SubscribeAsync_recovers_from_a_concurrent_customer_create_conflict()
    {
        var lookupCount = 0;
        var handler = new StubHttpMessageHandler(r =>
        {
            if (IsProducts(r)) return (HttpStatusCode.OK, ProductsListJson);
            if (IsLookup(r))
            {
                // First lookup misses; after the racing create loses (422), the re-read finds the customer.
                lookupCount++;
                return lookupCount == 1
                    ? (HttpStatusCode.NotFound, "{}")
                    : (HttpStatusCode.OK, CustomerJson(555));
            }
            if (IsCreateCustomer(r)) return (HttpStatusCode.UnprocessableEntity, """{ "errors": ["Reference: has already been taken"] }""");
            if (IsListSubs(r)) return (HttpStatusCode.OK, "[]");
            if (IsCreateSub(r)) return (HttpStatusCode.Created, SubscriptionJson(7, "active", "eshop-pro"));
            return (HttpStatusCode.InternalServerError, "{}");
        });
        var service = BuildService(handler);

        var result = await service.SubscribeAsync(UserName, "eshop-pro");

        Assert.False(result.AlreadyActive);
        Assert.Equal(7, result.Subscription.Id);
        Assert.Equal(2, lookupCount); // read-before-create, then re-read after the 422 conflict
    }

    [Fact]
    public async Task GetSubscriptionsAsync_returns_empty_when_no_customer_exists()
    {
        var handler = new StubHttpMessageHandler(r =>
            IsLookup(r) ? (HttpStatusCode.NotFound, "{}") : (HttpStatusCode.InternalServerError, "{}"));
        var service = BuildService(handler);

        var subscriptions = await service.GetSubscriptionsAsync(UserName);

        Assert.Empty(subscriptions);
        Assert.Equal(0, handler.CountOf(HttpMethod.Get, "/subscriptions.json"));
    }

    [Fact]
    public async Task GetSubscriptionsAsync_maps_provider_subscriptions()
    {
        var handler = new StubHttpMessageHandler(r =>
        {
            if (IsLookup(r)) return (HttpStatusCode.OK, CustomerJson(555));
            if (IsListSubs(r)) return (HttpStatusCode.OK, $"[{SubscriptionJson(11, "active", "eshop-pro")}]");
            return (HttpStatusCode.InternalServerError, "{}");
        });
        var service = BuildService(handler);

        var subscriptions = await service.GetSubscriptionsAsync(UserName);

        var sub = Assert.Single(subscriptions);
        Assert.Equal(11, sub.Id);
        Assert.Equal("active", sub.State);
        Assert.Equal("eshop-pro", sub.PlanHandle);
    }
}
