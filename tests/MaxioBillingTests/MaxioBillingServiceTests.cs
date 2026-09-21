using System.Net;
using MaxioAdvancedBilling;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Microsoft.eShopWeb.MaxioBillingTests;

public class MaxioBillingServiceTests
{
    private const string FamilyHandle = "eshop-subscribe";
    private const string UserRef = "user-guid-123";

    private const string FamiliesJson = """[{"product_family":{"id":3026728,"handle":"eshop-subscribe","name":"eShopSubscribe"}}]""";

    private const string PlansJson = """
        [
          {"product":{"id":7126957,"name":"Pro Plan","handle":"eshop-pro","description":"Pro tier","price_in_cents":29900,"interval":1,"interval_unit":"month"}},
          {"product":{"id":7126958,"name":"Basic Plan","handle":"basic-plan","price_in_cents":2900,"interval":1,"interval_unit":"month"}}
        ]
        """;

    private const string CustomerJson = """{"customer":{"id":555,"reference":"user-guid-123","email":"demouser@microsoft.com"}}""";

    private const string ActiveProSubscriptionJson = """
        [{"subscription":{"id":9001,"state":"active","product":{"handle":"eshop-pro","name":"Pro Plan"},
          "product_price_in_cents":29900,"current_period_ends_at":"2026-10-22T00:00:00+00:00",
          "next_assessment_at":"2026-10-22T00:00:00+00:00","created_at":"2026-09-22T00:00:00+00:00"}}]
        """;

    private const string CreatedSubscriptionJson = """
        {"subscription":{"id":9002,"state":"active","product":{"handle":"eshop-pro","name":"Pro Plan"},
          "product_price_in_cents":29900,"next_assessment_at":"2026-10-22T00:00:00+00:00"}}
        """;

    private static MaxioBillingService NewService(ScriptedHandler handler)
    {
        var client = new MaxioAdvancedBillingClient(new HttpClient(handler), new MaxioAdvancedBillingClientOptions());
        var settings = new MaxioSettings { ApiKey = "k", Subdomain = "test", ProductFamilyHandle = FamilyHandle };
        return new MaxioBillingService(client, settings, NullLogger<MaxioBillingService>.Instance);
    }

    private static bool IsGet(HttpRequestMessage r, string suffix) =>
        r.Method == HttpMethod.Get && (r.RequestUri?.AbsolutePath.EndsWith(suffix) ?? false);

    private static bool IsGetContaining(HttpRequestMessage r, string fragment) =>
        r.Method == HttpMethod.Get && (r.RequestUri?.AbsolutePath.Contains(fragment) ?? false);

    private static bool IsPost(HttpRequestMessage r, string suffix) =>
        r.Method == HttpMethod.Post && (r.RequestUri?.AbsolutePath.EndsWith(suffix) ?? false);

    private static HttpResponseMessage PlanCatalogResponder(HttpRequestMessage r)
    {
        if (IsGet(r, "/product_families.json")) return ScriptedHandler.Json(HttpStatusCode.OK, FamiliesJson);
        if (IsGetContaining(r, "/product_families/")) return ScriptedHandler.Json(HttpStatusCode.OK, PlansJson);
        return ScriptedHandler.Json(HttpStatusCode.InternalServerError, "{}");
    }

    [Fact]
    public async Task GetPlansAsync_maps_products_in_the_family()
    {
        var handler = new ScriptedHandler(PlanCatalogResponder);
        var service = NewService(handler);

        var plans = await service.GetPlansAsync();

        Assert.Equal(2, plans.Count);
        var pro = plans.Single(p => p.Handle == "eshop-pro");
        Assert.Equal("Pro Plan", pro.Name);
        Assert.Equal(29900, pro.PriceInCents);
        Assert.Equal(1, pro.IntervalCount);
        Assert.Equal("month", pro.IntervalUnit);
        Assert.Equal(7126957, pro.ProductId);
    }

    [Fact]
    public async Task SubscribeAsync_creates_customer_and_subscription_when_none_exist()
    {
        var handler = new ScriptedHandler(r =>
        {
            if (IsGet(r, "/product_families.json")) return ScriptedHandler.Json(HttpStatusCode.OK, FamiliesJson);
            if (IsGetContaining(r, "/product_families/")) return ScriptedHandler.Json(HttpStatusCode.OK, PlansJson);
            if (IsGet(r, "/customers/lookup.json")) return ScriptedHandler.Json(HttpStatusCode.NotFound, "{}"); // no customer yet
            if (IsPost(r, "/customers.json")) return ScriptedHandler.Json(HttpStatusCode.Created, CustomerJson);
            if (IsGet(r, "/subscriptions.json") && (r.RequestUri!.AbsolutePath.Contains("/customers/")))
                return ScriptedHandler.Json(HttpStatusCode.OK, "[]"); // no existing subscriptions
            if (IsPost(r, "/subscriptions.json")) return ScriptedHandler.Json(HttpStatusCode.Created, CreatedSubscriptionJson);
            return ScriptedHandler.Json(HttpStatusCode.InternalServerError, "{}");
        });
        var service = NewService(handler);

        var outcome = await service.SubscribeAsync(NewRequest("eshop-pro"));

        Assert.True(outcome.WasCreated);
        Assert.Equal(9002, outcome.Subscription.Id);
        Assert.Equal("active", outcome.Subscription.State);
        Assert.Equal("eshop-pro", outcome.Subscription.PlanHandle);
        Assert.Equal(1, handler.Count(HttpMethod.Post, "/customers.json"));
        Assert.Equal(1, handler.Count(HttpMethod.Post, "/subscriptions.json"));
    }

    [Fact]
    public async Task SubscribeAsync_is_idempotent_when_a_live_subscription_exists()
    {
        var handler = new ScriptedHandler(r =>
        {
            if (IsGet(r, "/product_families.json")) return ScriptedHandler.Json(HttpStatusCode.OK, FamiliesJson);
            if (IsGetContaining(r, "/product_families/")) return ScriptedHandler.Json(HttpStatusCode.OK, PlansJson);
            if (IsGet(r, "/customers/lookup.json")) return ScriptedHandler.Json(HttpStatusCode.OK, CustomerJson); // customer exists
            if (IsGet(r, "/subscriptions.json") && r.RequestUri!.AbsolutePath.Contains("/customers/"))
                return ScriptedHandler.Json(HttpStatusCode.OK, ActiveProSubscriptionJson); // already subscribed
            return ScriptedHandler.Json(HttpStatusCode.InternalServerError, "{}");
        });
        var service = NewService(handler);

        var outcome = await service.SubscribeAsync(NewRequest("eshop-pro"));

        Assert.False(outcome.WasCreated);
        Assert.Equal(9001, outcome.Subscription.Id);
        // No customer or subscription writes: fully idempotent.
        Assert.Equal(0, handler.Count(HttpMethod.Post, "/customers.json"));
        Assert.Equal(0, handler.Count(HttpMethod.Post, "/subscriptions.json"));
    }

    [Fact]
    public async Task SubscribeAsync_rejects_unknown_plan_with_invalid_request()
    {
        var handler = new ScriptedHandler(PlanCatalogResponder);
        var service = NewService(handler);

        var ex = await Assert.ThrowsAsync<SubscriptionBillingException>(
            () => service.SubscribeAsync(NewRequest("does-not-exist")));

        Assert.Equal(SubscriptionBillingErrorKind.InvalidRequest, ex.Kind);
        Assert.Equal(0, handler.Count(HttpMethod.Post, "/subscriptions.json"));
    }

    [Fact]
    public async Task GetSubscriptionsAsync_returns_empty_when_customer_not_found()
    {
        var handler = new ScriptedHandler(r =>
            IsGet(r, "/customers/lookup.json")
                ? ScriptedHandler.Json(HttpStatusCode.NotFound, "{}")
                : ScriptedHandler.Json(HttpStatusCode.InternalServerError, "{}"));
        var service = NewService(handler);

        var subs = await service.GetSubscriptionsAsync(UserRef);

        Assert.Empty(subs);
    }

    [Fact]
    public async Task GetSubscriptionsAsync_maps_next_billing_date()
    {
        var handler = new ScriptedHandler(r =>
        {
            if (IsGet(r, "/customers/lookup.json")) return ScriptedHandler.Json(HttpStatusCode.OK, CustomerJson);
            if (IsGet(r, "/subscriptions.json")) return ScriptedHandler.Json(HttpStatusCode.OK, ActiveProSubscriptionJson);
            return ScriptedHandler.Json(HttpStatusCode.InternalServerError, "{}");
        });
        var service = NewService(handler);

        var subs = await service.GetSubscriptionsAsync(UserRef);

        var sub = Assert.Single(subs);
        Assert.Equal("active", sub.State);
        Assert.Equal("eshop-pro", sub.PlanHandle);
        Assert.Equal(29900, sub.PriceInCents);
        Assert.Equal(new DateTimeOffset(2026, 10, 22, 0, 0, 0, TimeSpan.Zero), sub.NextBillingAt);
    }

    [Fact]
    public async Task CreateCustomer_post_is_not_resent_on_transport_failure()
    {
        var handler = new ScriptedHandler(r =>
        {
            if (IsGet(r, "/product_families.json")) return ScriptedHandler.Json(HttpStatusCode.OK, FamiliesJson);
            if (IsGetContaining(r, "/product_families/")) return ScriptedHandler.Json(HttpStatusCode.OK, PlansJson);
            if (IsGet(r, "/customers/lookup.json")) return ScriptedHandler.Json(HttpStatusCode.NotFound, "{}");
            if (IsPost(r, "/customers.json")) throw new HttpRequestException("connection reset");
            return ScriptedHandler.Json(HttpStatusCode.InternalServerError, "{}");
        });
        var service = NewService(handler);

        var ex = await Assert.ThrowsAsync<SubscriptionBillingException>(
            () => service.SubscribeAsync(NewRequest("eshop-pro")));

        Assert.Equal(SubscriptionBillingErrorKind.ProviderUnavailable, ex.Kind);
        // The SDK never resends a POST — a duplicate customer is not created.
        Assert.Equal(1, handler.Count(HttpMethod.Post, "/customers.json"));
    }

    private static SubscribeRequest NewRequest(string planHandle) => new()
    {
        UserReference = UserRef,
        Email = "demouser@microsoft.com",
        FirstName = "demouser",
        LastName = "(eShopOnWeb)",
        PlanHandle = planHandle
    };
}
